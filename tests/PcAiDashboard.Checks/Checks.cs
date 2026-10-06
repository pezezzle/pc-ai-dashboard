using System.Globalization;
using System.Text.Json;
using PcAiDashboard;

static class Checks
{
    public static int Run()
    {
        int count=0;
        void Assert(bool test,string name) { if(!test) throw new Exception(name); Console.WriteLine("PASS "+name); count++; }
        var bytes=new byte[327]; bytes[0x3d]=0x0e; bytes[0x3e]=0xa6;
        Assert(HardwareService.OctoTemperature(bytes,0)==37.5,"OCTO temperature is big-endian in hundredths");
        bytes[0x3f]=0x7f; bytes[0x40]=0xff;
        Assert(HardwareService.OctoTemperature(bytes,1)==null,"Disconnected temperature is unavailable");
        bytes[0xe0]=0x07; bytes[0xe1]=0xd0;
        Assert(HardwareService.OctoRpm(bytes,7)==2000,"Last OCTO fan channel is decoded correctly");
        Assert(HardwareService.OctoRpm([],0)==null && HardwareService.OctoTemperature(bytes,5)==null,"Truncated report and invalid channel are rejected");
        bytes[0x7d]=0x11;bytes[0x7e]=0xf4;bytes[0xd8]=0x27;bytes[0xd9]=0x10;
        Assert(HardwareService.OctoDuty(bytes,0)==45.96 && HardwareService.OctoDuty(bytes,7)==100,"Actual OCTO PWM decodes in centi-percent with the correct channel stride");
        bytes[0x7d]=0xff;bytes[0x7e]=0xff;
        Assert(HardwareService.OctoDuty(bytes,0)==null && HardwareService.OctoDuty([],0)==null,"Invalid and truncated PWM data stays unavailable rather than showing 100 percent");
        var now=DateTimeOffset.Now;
        string Export(DateTimeOffset time,string value)=>$"<LogDataExport><ExportTime>{time:O}</ExportTime><Logdata><LogDataSet><name>CPU Package</name><value>{value}</value><unit>°C</unit></LogDataSet></Logdata></LogDataExport>";
        CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("de-DE");
        Assert(HardwareService.ParseExport("\uFEFF"+Export(now,"57.5"),now).Single().Value==57.5,"Export handles BOM and German Windows culture");
        Assert(HardwareService.ParseExport(Export(now.AddSeconds(-12),"57"),now).Count==0,"Stale Aquasuite export never appears live");
        Assert(HardwareService.ParseExport(Export(now,"NaN"),now).Single().Value==null,"Non-finite measurements are unavailable");
        using var codex=JsonDocument.Parse("""{"rateLimits":{"primary":{"usedPercent":1}},"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":100,"windowDurationMins":10080,"resetsAt":1791580407},"secondary":null}}}""");
        var quotas=AiService.ParseCodexLimits(codex.RootElement);
        Assert(quotas.Count==1 && quotas[0].Label=="7 Tage" && quotas[0].UsedPercent==100,"Codex multi-bucket quotas take precedence without duplicates");
        using var absent=JsonDocument.Parse("""{"rateLimits":{"primary":null,"secondary":null}}""");
        Assert(AiService.ParseCodexLimits(absent.RootElement).Count==0,"Missing limit is not displayed as zero usage");
        using var credits=JsonDocument.Parse("""{"rateLimits":{"credits":{"hasCredits":true,"unlimited":false,"balance":"44651.0274592500"}}}""");
        Assert(AiService.ParseCodexCredits(credits.RootElement)?.Balance==44651.02745925,"Codex decimal credit strings parse independently of German Windows culture");
        using var unlimited=JsonDocument.Parse("""{"rateLimitsByLimitId":{"codex":{"credits":{"hasCredits":true,"unlimited":true,"balance":null}}}}""");
        Assert(AiService.ParseCodexCredits(unlimited.RootElement)?.Unlimited==true && AiService.ParseCodexCredits(absent.RootElement)==null,"Unlimited and missing Codex credits remain distinct");
        using var claude=JsonDocument.Parse("""{"five_hour":{"utilization":22.5,"resets_at":"2026-10-09T21:13:27Z"},"seven_day":null,"seven_day_sonnet":{"utilization":48,"resets_at":null}}""");
        var cq=AiService.ParseClaudeLimits(claude.RootElement);
        Assert(cq.Count==2 && cq[0].ResetsAt==1791580407 && cq[1].ResetsAt==null,"Claude scoped windows and optional resets are preserved");
        using var money=JsonDocument.Parse("""{"spend":{"enabled":true,"used":{"amount_minor":1267,"currency":"USD","exponent":2},"limit":{"amount_minor":4000,"currency":"USD","exponent":2},"balance":{"amount_minor":10000,"currency":"USD","exponent":2}}}""");
        var paid=AiService.ParseClaudeCredits(money.RootElement);
        Assert(paid?.Balance==100 && paid.Spent==12.67 && paid.Limit==40 && paid.Unit=="USD","Claude funded balance is separate from monthly spend headroom");
        using var hidden=JsonDocument.Parse("""{"extra_usage":{"is_enabled":false,"monthly_limit":13500,"used_credits":0,"currency":"USD","decimal_places":2},"spend":{"enabled":false,"used":{"amount_minor":0,"currency":"USD","exponent":2},"limit":{"amount_minor":13500,"currency":"USD","exponent":2},"balance":null}}""");
        var unknown=AiService.ParseClaudeCredits(hidden.RootElement);
        Assert(unknown?.Balance==null && unknown?.Limit==135 && unknown.Enabled==false,"Missing prepaid balance is never inferred from a Claude monthly cap");
        using var legacy=JsonDocument.Parse("""{"extra_usage":{"is_enabled":true,"monthly_limit":1000,"used_credits":234.5,"currency":"USD","decimal_places":2}}""");
        Assert(AiService.ParseClaudeCredits(legacy.RootElement)?.Spent==2.345 && AiService.ParseClaudeCredits(claude.RootElement)==null,"Legacy Claude minor units convert without inventing an absent credit row");
        using var negative=JsonDocument.Parse("""{"spend":{"balance":{"amount_minor":-27,"currency":"USD","exponent":2}}}""");
        Assert(AiService.ParseClaudeCredits(negative.RootElement)?.Balance==-.27,"A provider-reported negative money balance is preserved");
        var s=DashboardSettings.Validate(new() { PumpChannel=100,CaseTempChannel=-1,Volume=-8,Dim=2,Accent="bad",TextColor="bad",BackgroundMode="bad" });
        Assert(s.PumpChannel==7 && s.CaseTempChannel==0 && s.Volume==0 && s.Dim==.95 && s.Accent=="#66e7c8" && s.BackgroundMode=="gradient","Settings constrain hardware channels and visual values");
        Assert(s.TextColor=="#ecf3f6", "Invalid text color falls back to readable default");
        Assert(new DashboardSettings().TemperatureScaleMax==100 && DashboardSettings.Validate(new(){TemperatureScaleMax=0}).TemperatureScaleMax==40,"Temperature scale has an explicit degree-based default and validated bounds");
        var saved=JsonSerializer.Serialize(new DashboardSettings { Accent="#ff00ff", TextColor="#eecc88" },AppFiles.Json);
        var restored=JsonSerializer.Deserialize<DashboardSettings>(saved,AppFiles.Json)!;
        Assert(restored.Accent=="#ff00ff" && restored.TextColor=="#eecc88", "Both colors survive settings serialization");
        const long large=7_430_854_813;
        Assert(LocalVideoServer.TryRange("bytes=6000000000-6000000031",large,out var start,out var end) && start==6_000_000_000 && end==6_000_000_031,"Media ranges preserve offsets beyond 4 GB");
        Assert(LocalVideoServer.TryRange("bytes=-32",large,out start,out end) && start==large-32 && end==large-1,"Suffix media range can read the end of a large MP4");
        Assert(!LocalVideoServer.TryRange("bytes=99999999999999999999-",large,out _,out _) && !LocalVideoServer.TryRange("bytes=5-2",large,out _,out _),"Overflow and backwards media ranges are rejected");
        CheckMediaHttp(Assert).GetAwaiter().GetResult();
        Console.WriteLine($"{count} checks passed."); return 0;
    }
    private static async Task CheckMediaHttp(Action<bool,string> Assert)
    {
        var fixture=System.IO.Path.GetTempFileName();
        try {
            System.IO.File.WriteAllBytes(fixture,[0,1,2,3,4,5,6,7]);
            using var server=new LocalVideoServer("https://pc-ai-dashboard.local"); server.Configure(fixture);
            using var http=new HttpClient { Timeout=TimeSpan.FromSeconds(5) };
            using var request=new HttpRequestMessage(HttpMethod.Get,server.Url);
            request.Headers.Range=new System.Net.Http.Headers.RangeHeaderValue(2,4);
            using var response=await http.SendAsync(request);
            var body=await response.Content.ReadAsByteArrayAsync();
            Assert(response.StatusCode==System.Net.HttpStatusCode.PartialContent && body.SequenceEqual(new byte[] {2,3,4}) && response.Content.Headers.ContentRange?.To==4,"Loopback streams only requested bytes with a correct 206 response");
            using var head=await http.SendAsync(new HttpRequestMessage(HttpMethod.Head,server.Url));
            Assert(head.Content.Headers.ContentLength==8 && (await head.Content.ReadAsByteArrayAsync()).Length==0,"HEAD reports full video size without reading its body");
            using var bad=new HttpRequestMessage(HttpMethod.Get,server.Url); bad.Headers.Range=new System.Net.Http.Headers.RangeHeaderValue(80,null);
            using var outside=await http.SendAsync(bad);
            Assert(outside.StatusCode==System.Net.HttpStatusCode.RequestedRangeNotSatisfiable && outside.Content.Headers.ContentRange?.Length==8,"Invalid media range returns 416 and the actual file size");
            using var foreign=new HttpRequestMessage(HttpMethod.Get,server.Url); foreign.Headers.Add("Origin","https://example.com");
            using var denied=await http.SendAsync(foreign);
            using var unknown=await http.GetAsync(new Uri(new Uri(server.Url),"/unknown"));
            Assert(denied.StatusCode==System.Net.HttpStatusCode.Forbidden && unknown.StatusCode==System.Net.HttpStatusCode.NotFound,"Foreign origins and unknown paths cannot read the selected video");
        }finally {System.IO.File.Delete(fixture);}
    }
}
