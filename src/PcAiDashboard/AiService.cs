using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Globalization;
using System.Net;

namespace PcAiDashboard;

public sealed class AiService : IDisposable
{
    private readonly CancellationTokenSource stop=new();
    private readonly HttpClient http=new() { Timeout=TimeSpan.FromSeconds(15) };
    private readonly object gate=new();
    private AiUsage codex=new("Codex","Verbinden",[],[]),claude=new("Claude","Verbinden",[],[]);
    private static readonly string Home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private DateTimeOffset claudeRetryAt;
    public AiService()=>_=Task.Run(Poll);
    public List<AiUsage> Read() { lock(gate) return [codex,claude]; }
    private async Task Poll()
    {
        while(!stop.IsCancellationRequested)
        {
            await Task.WhenAll(UpdateCodex(),UpdateClaude());
            try { await Task.Delay(TimeSpan.FromSeconds(60),stop.Token); } catch(OperationCanceledException) { break; }
        }
    }
    private async Task UpdateCodex()
    {
        var sessions=ReadCodexSessions();
        try
        {
            var exe=FindCodex();
            if(exe==null) throw new FileNotFoundException("Codex CLI fehlt");
            using var process=new Process { StartInfo=new(exe) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true } };
            process.StartInfo.ArgumentList.Add("app-server");
            process.StartInfo.ArgumentList.Add("-c"); process.StartInfo.ArgumentList.Add("mcp_servers={}");
            process.Start(); var errorDrain=process.StandardError.ReadToEndAsync();
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(20000);
            try
            {
                await process.StandardInput.WriteLineAsync("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"pc_ai_dashboard\",\"version\":\"1.0.0\"}}}"); await process.StandardInput.FlushAsync();
                await ReadResponse(process,1,timeout.Token);
                await process.StandardInput.WriteLineAsync("{\"method\":\"initialized\",\"params\":{}}");
                await process.StandardInput.WriteLineAsync("{\"id\":2,\"method\":\"account/rateLimits/read\"}"); await process.StandardInput.FlushAsync();
                var response=await ReadResponse(process,2,timeout.Token);
                if(response.TryGetProperty("error",out _)) throw new InvalidOperationException("Codex-Anmeldung prüfen");
                var quotas=ParseCodexLimits(response.GetProperty("result"));
                lock(gate) codex=new("Codex","Live",quotas,sessions,DateTimeOffset.Now,quotas.Count==0?"Kein Zeitlimit vom Account geliefert":null,ParseCodexCredits(response.GetProperty("result")));
            }
            finally { if(!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); await errorDrain; }
        }
        catch(Exception e) when(!stop.IsCancellationRequested)
        {
            lock(gate) codex=codex with { Status="Nicht erreichbar",Sessions=sessions,Detail=e is FileNotFoundException?"Codex CLI installieren und anmelden":"Codex-Anmeldung oder Verbindung prüfen" };
        }
    }
    public static string? FindCodex()
    {
        var npm=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"npm","node_modules","@openai");
        if(Directory.Exists(npm)) { var exe=Directory.EnumerateFiles(npm,"codex.exe",SearchOption.AllDirectories).FirstOrDefault(); if(exe!=null) return exe; }
        foreach(var dir in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator)) { var candidate=Path.Combine(dir,"codex.exe"); if(File.Exists(candidate)) return candidate; }
        return null;
    }
    private static async Task<JsonElement> ReadResponse(Process p,int id,CancellationToken token)
    {
        while(true) { var line=await p.StandardOutput.ReadLineAsync(token) ?? throw new IOException("Codex-Verbindung beendet"); try { using var d=JsonDocument.Parse(line); if(d.RootElement.TryGetProperty("id",out var rid) && rid.ValueKind==JsonValueKind.Number && rid.GetInt32()==id) return d.RootElement.Clone(); }catch(JsonException) { } }
    }
    public static List<Quota> ParseCodexLimits(JsonElement result)
    {
        var quotas=new List<Quota>();
        if(result.TryGetProperty("rateLimitsByLimitId",out var buckets) && buckets.ValueKind==JsonValueKind.Object)
        {
            foreach(var bucket in buckets.EnumerateObject()) AddBucket(bucket.Value,bucket.Name);
        }
        else if(result.TryGetProperty("rateLimits",out var single)) AddBucket(single,"codex");
        return quotas;
        void AddBucket(JsonElement b,string bucketName)
        {
            foreach(var key in new[]{"primary","secondary"}) if(b.ValueKind==JsonValueKind.Object && b.TryGetProperty(key,out var w) && w.ValueKind==JsonValueKind.Object && w.TryGetProperty("usedPercent",out var used) && used.TryGetDouble(out var percent))
            {
                int? mins=w.TryGetProperty("windowDurationMins",out var m)&&m.TryGetInt32(out var v)?v:null;
                long? reset=w.TryGetProperty("resetsAt",out var r)&&r.TryGetInt64(out var epoch)?epoch:null;
                var label=mins==10080?"7 Tage":mins==300?"5 Stunden":mins.HasValue?$"{mins} Minuten":key=="primary"?"Limit":"Weiteres Limit";
                if(bucketName!="codex") label=bucketName+" · "+label;
                quotas.Add(new(label,Math.Clamp(percent,0,100),reset,mins));
            }
        }
    }
    private async Task UpdateClaude()
    {
        var sessions=ReadClaudeSessions();
        if(DateTimeOffset.UtcNow<claudeRetryAt) { lock(gate) claude=claude with { Sessions=sessions }; return; }
        try
        {
            var credentials=ClaudeCredentials.Find() ?? throw new IOException("Keine gültige Claude-Anmeldung");
            var access=credentials.Token;
            using var req=new HttpRequestMessage(HttpMethod.Get,"https://api.anthropic.com/api/oauth/usage");
            req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",access); req.Headers.Add("anthropic-beta","oauth-2025-04-20"); req.Headers.UserAgent.ParseAdd("PC-AI-Dashboard/1.0");
            using var response=await http.SendAsync(req,stop.Token);
            if(response.StatusCode==HttpStatusCode.TooManyRequests)
            {
                var retry=response.Headers.RetryAfter;
                claudeRetryAt=retry?.Date??DateTimeOffset.UtcNow.Add(retry?.Delta??TimeSpan.FromMinutes(3));
                if(claudeRetryAt<DateTimeOffset.UtcNow.AddSeconds(60)) claudeRetryAt=DateTimeOffset.UtcNow.AddSeconds(60);
                lock(gate) claude=claude with {Status="Abrufpause",Sessions=sessions,Detail="Claude begrenzt die Abfragen; letzter bekannter Stand"};
                return;
            }
            response.EnsureSuccessStatusCode();
            using var body=JsonDocument.Parse(await response.Content.ReadAsStringAsync(stop.Token));
            var quotas=ParseClaudeLimits(body.RootElement);
            lock(gate) claude=new("Claude","Live",quotas,sessions,DateTimeOffset.Now,quotas.Count==0?"Keine Limits vom Account geliefert":credentials.Source,ParseClaudeCredits(body.RootElement));
        }
        catch(Exception) when(!stop.IsCancellationRequested)
        {
            var local=ReadClaudeStatusLimits();
            lock(gate) claude=local!=null?claude with {Name="Claude",Status="Sitzungsdaten",Quotas=local.Value.Quotas,Sessions=sessions,Detail="Werte aus Claude Code; Guthaben ist letzter bekannter Stand"}:claude with { Status="Nicht erreichbar",Sessions=sessions,Detail="Claude Code anmelden; lokale Statuszeile liefert zusätzlich Kontextdaten" };
        }
    }
    public static List<Quota> ParseClaudeLimits(JsonElement data)
    {
        var q=new List<Quota>();
        foreach(var field in data.EnumerateObject())
        {
            if(field.Name!="five_hour" && !field.Name.StartsWith("seven_day",StringComparison.Ordinal)) continue;
            if(field.Value.ValueKind!=JsonValueKind.Object || !field.Value.TryGetProperty("utilization",out var u) || !u.TryGetDouble(out var used)) continue;
            var label=field.Name switch { "five_hour"=>"5 Stunden", "seven_day"=>"7 Tage", "seven_day_sonnet"=>"7 Tage · Sonnet", "seven_day_opus"=>"7 Tage · Opus", _=>"7 Tage · "+field.Name[9..].Replace('_',' ') };
            long? reset=field.Value.TryGetProperty("resets_at",out var r) && DateTimeOffset.TryParse(r.GetString(),out var t)?t.ToUnixTimeSeconds():null;
            q.Add(new(label,Math.Clamp(used,0,100),reset));
        }
        return q;
    }
    public static CreditUsage? ParseCodexCredits(JsonElement data)
    {
        JsonElement bucket;
        if(data.TryGetProperty("rateLimitsByLimitId",out var buckets) && buckets.ValueKind==JsonValueKind.Object && buckets.TryGetProperty("codex",out bucket)) { }
        else if(!data.TryGetProperty("rateLimits",out bucket)) return null;
        if(bucket.ValueKind!=JsonValueKind.Object || !bucket.TryGetProperty("credits",out var credits) || credits.ValueKind!=JsonValueKind.Object) return null;
        bool unlimited=credits.TryGetProperty("unlimited",out var u) && u.ValueKind==JsonValueKind.True;
        bool has=credits.TryGetProperty("hasCredits",out var h) && h.ValueKind==JsonValueKind.True;
        double? balance=Number(credits,"balance",true);
        return new("Live",balance,"Credits",unlimited,Detail:unlimited?null:balance.HasValue?null:has?"Guthaben vorhanden; Betrag nicht bereitgestellt":"Kein Credits-Guthaben");
    }
    public static CreditUsage? ParseClaudeCredits(JsonElement data)
    {
        bool hasExtra=data.TryGetProperty("extra_usage",out var extra) && extra.ValueKind==JsonValueKind.Object;
        if(data.TryGetProperty("spend",out var spend) && spend.ValueKind==JsonValueKind.Object)
        {
            var balance=Money(spend,"balance",true); var used=Money(spend,"used"); var limit=Money(spend,"limit");
            string? spendUnit=balance?.Currency??used?.Currency??limit?.Currency;
            if(spendUnit!=null)
            {
                bool active=spend.TryGetProperty("enabled",out var enabledValue)&&enabledValue.ValueKind==JsonValueKind.True;
                return new("Live",balance?.Value,spendUnit,Spent:used?.Currency==spendUnit?used?.Value:null,
                    Limit:limit?.Currency==spendUnit?limit?.Value:null,Enabled:active,Detail:balance==null?"Restguthaben nicht vom Anbieter bereitgestellt":null);
            }
        }
        if(!hasExtra) return null;
        bool enabled=extra.TryGetProperty("is_enabled",out var e)&&e.ValueKind==JsonValueKind.True;
        string unit=extra.TryGetProperty("currency",out var currency)&&currency.ValueKind==JsonValueKind.String?currency.GetString()!:"USD";
        int places=extra.TryGetProperty("decimal_places",out var decimals)&&decimals.TryGetInt32(out var p)&&p is >=0 and <=6?p:2;
        double divisor=Math.Pow(10,places);
        return new("Live",null,unit,Spent:Divide(Number(extra,"used_credits")),Limit:Divide(Number(extra,"monthly_limit")),Enabled:enabled,Detail:"Restguthaben nicht vom Anbieter bereitgestellt");
        double? Divide(double? value)=>value.HasValue?value/divisor:null;
    }
    private static (double Value,string Currency)? Money(JsonElement obj,string name,bool allowNegative=false)
    {
        if(!obj.TryGetProperty(name,out var money) || money.ValueKind!=JsonValueKind.Object ||
            !money.TryGetProperty("currency",out var currency) || currency.ValueKind!=JsonValueKind.String ||
            !money.TryGetProperty("exponent",out var exponent) || !exponent.TryGetInt32(out var places) || places is <0 or >6) return null;
        var raw=Number(money,"amount_minor",allowNegative); var unit=currency.GetString();
        if(raw==null || !System.Text.RegularExpressions.Regex.IsMatch(unit??"","^[A-Z]{3}$")) return null;
        return (raw.Value/Math.Pow(10,places),unit!);
    }
    private static double? Number(JsonElement obj,string name,bool allowNegative=false)
    {
        if(!obj.TryGetProperty(name,out var v)) return null;
        double number=0;
        bool parsed=v.ValueKind==JsonValueKind.Number?v.TryGetDouble(out number):v.ValueKind==JsonValueKind.String&&double.TryParse(v.GetString(),NumberStyles.Float,CultureInfo.InvariantCulture,out number);
        return parsed && double.IsFinite(number) && (allowNegative || number>=0)?number:null;
    }
    private static List<SessionUsage> ReadCodexSessions()
    {
        var list=new List<SessionUsage>(); var root=Path.Combine(Environment.GetEnvironmentVariable("CODEX_HOME")??Path.Combine(Home,".codex"),"sessions");
        if(!Directory.Exists(root)) return list;
        try
        {
            foreach(var f in Directory.EnumerateFiles(root,"*.jsonl",SearchOption.AllDirectories).Select(p=>new FileInfo(p)).OrderByDescending(f=>f.LastWriteTimeUtc).Take(8))
            {
                try
                {
                    using var stream=new FileStream(f.FullName,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                    // Only usage events from the tail; conversation text is neither retained nor displayed.
                    stream.Seek(Math.Max(0,stream.Length-512_000),SeekOrigin.Begin); using var reader=new StreamReader(stream); var lines=reader.ReadToEnd().Split('\n');
                    foreach(var line in lines.Reverse())
                    {
                        if(!line.Contains("token_count",StringComparison.Ordinal)) continue;
                        using var d=JsonDocument.Parse(line); var p=d.RootElement.GetProperty("payload"); if(p.GetProperty("type").GetString()!="token_count") continue;
                        var info=p.GetProperty("info"); if(info.ValueKind!=JsonValueKind.Object) continue;
                        var last=info.GetProperty("last_token_usage"); long tokens=last.GetProperty("input_tokens").GetInt64();
                        long? cap=info.TryGetProperty("model_context_window",out var c)&&c.TryGetInt64(out var size)?size:null;
                        var id=f.Name.Replace(".jsonl",""); var label="Chat "+id[^Math.Min(8,id.Length)..];
                        list.Add(new(id,label,cap>0?Math.Clamp(100.0*tokens/cap.Value,0,100):null,tokens,cap,new DateTimeOffset(f.LastWriteTimeUtc))); break;
                    }
                }catch(Exception e) when(e is IOException or JsonException or KeyNotFoundException or InvalidOperationException) { }
            }
        }catch(IOException) { }
        return list;
    }
    private static List<SessionUsage> ReadClaudeSessions()
    {
        var list=new List<SessionUsage>(); var root=Path.Combine(AppFiles.Root,"claude-sessions"); if(!Directory.Exists(root)) return list;
        foreach(var f in Directory.EnumerateFiles(root,"*.json").Select(p=>new FileInfo(p)).OrderByDescending(f=>f.LastWriteTimeUtc).Take(8)) try
        {
            using var d=JsonDocument.Parse(File.ReadAllText(f.FullName)); var r=d.RootElement; var cw=r.GetProperty("context_window");
            double? used=cw.TryGetProperty("used_percentage",out var u)&&u.TryGetDouble(out var value)?value:null;
            long? cap=cw.TryGetProperty("context_window_size",out var c)&&c.TryGetInt64(out var size)?size:null;
            long? tokens=null; if(cw.TryGetProperty("current_usage",out var cu)&&cu.ValueKind==JsonValueKind.Object) { tokens=0; foreach(var key in new[]{"input_tokens","cache_creation_input_tokens","cache_read_input_tokens"}) if(cu.TryGetProperty(key,out var n)&&n.TryGetInt64(out var count)) tokens+=count; }
            list.Add(new(r.GetProperty("session_id").GetString()??f.Name,r.TryGetProperty("label",out var name)?name.GetString()??"Chat":"Chat",used,tokens,cap,new DateTimeOffset(f.LastWriteTimeUtc)));
        }catch(Exception e) when(e is IOException or JsonException or KeyNotFoundException or InvalidOperationException) { }
        return list;
    }
    private static (List<Quota> Quotas,DateTimeOffset Time)? ReadClaudeStatusLimits()
    {
        var root=Path.Combine(AppFiles.Root,"claude-sessions"); if(!Directory.Exists(root)) return null;
        var f=Directory.EnumerateFiles(root,"*.json").Select(p=>new FileInfo(p)).OrderByDescending(f=>f.LastWriteTimeUtc).FirstOrDefault(); if(f==null) return null;
        try { using var d=JsonDocument.Parse(File.ReadAllText(f.FullName)); if(!d.RootElement.TryGetProperty("rate_limits",out var limits)||limits.ValueKind!=JsonValueKind.Object) return null; var q=new List<Quota>();
            foreach(var w in limits.EnumerateObject()) if(w.Value.ValueKind==JsonValueKind.Object && w.Value.TryGetProperty("used_percentage",out var p) && p.TryGetDouble(out var percent)) { long? reset=w.Value.TryGetProperty("resets_at",out var r)&&r.TryGetInt64(out var t)?t:null; if(reset<=DateTimeOffset.Now.ToUnixTimeSeconds()) continue; q.Add(new(w.Name=="five_hour"?"5 Stunden":w.Name=="seven_day"?"7 Tage":w.Name,percent,reset)); }
            return(q,new DateTimeOffset(f.LastWriteTimeUtc)); }catch(Exception) { return null; }
    }
    public void Dispose() { stop.Cancel(); http.Dispose(); }
}
