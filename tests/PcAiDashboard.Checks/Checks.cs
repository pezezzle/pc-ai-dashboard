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
        using var claude=JsonDocument.Parse("""{"five_hour":{"utilization":22.5,"resets_at":"2026-10-09T21:13:27Z"},"seven_day":null,"seven_day_sonnet":{"utilization":48,"resets_at":null}}""");
        var cq=AiService.ParseClaudeLimits(claude.RootElement);
        Assert(cq.Count==2 && cq[0].ResetsAt==1791580407 && cq[1].ResetsAt==null,"Claude scoped windows and optional resets are preserved");
        var s=DashboardSettings.Validate(new() { PumpChannel=100,CaseTempChannel=-1,Volume=-8,Dim=2,Accent="bad",BackgroundMode="bad" });
        Assert(s.PumpChannel==7 && s.CaseTempChannel==0 && s.Volume==0 && s.Dim==.95 && s.Accent=="#66e7c8" && s.BackgroundMode=="gradient","Settings constrain hardware channels and visual values");
        var fixture=System.IO.Path.GetTempFileName();
        try {
            System.IO.File.WriteAllBytes(fixture,[0,1,2,3,4,5,6,7]);
            using var segment=new MediaSegmentStream(fixture,2,3);var buffer=new byte[8];
            Assert(segment.Read(buffer,0,8)==3 && buffer[0]==2 && buffer[2]==4 && segment.Read(buffer,0,8)==0,"Video byte-range stream never reads beyond its range");
            segment.Seek(-1,System.IO.SeekOrigin.End);
            Assert(segment.ReadByte()==4 && segment.Length==3,"Video byte-range seeking uses relative positions");
        }finally {System.IO.File.Delete(fixture);}
        Console.WriteLine($"{count} checks passed."); return 0;
    }
}
