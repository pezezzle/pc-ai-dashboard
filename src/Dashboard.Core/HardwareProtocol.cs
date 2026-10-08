using System.Globalization;
using System.Xml.Linq;

namespace PcAiDashboard;

public static class HardwareProtocol
{
    public static double? OctoTemperature(byte[] report,int channel)
    {
        int index=0x3d+2*channel; if(channel is <0 or >3 || report.Length<index+2) return null; int v=report[index]*256+report[index+1]; return v==0x7fff?null:v/100.0;
    }
    public static double? OctoRpm(byte[] report,int channel)
    {
        int index=0x85+13*channel; if(channel is <0 or >7 || report.Length<index+2) return null; return report[index]*256+report[index+1];
    }
    public static double? OctoDuty(byte[] report,int channel)
    {
        int index=0x7d+13*channel; if(channel is <0 or >7 || report.Length<index+2) return null;
        int raw=report[index]*256+report[index+1]; return raw<=10000?raw/100.0:null;
    }
    public static List<Metric> ParseExport(string xml, DateTimeOffset now)
    {
        var root=XDocument.Parse(xml.TrimStart('\uFEFF','\0')).Root ?? throw new InvalidDataException();
        var timeText=root.Element("ExportTime")?.Value ?? root.Element("exportTime")?.Value;
        DateTimeOffset? time=DateTimeOffset.TryParse(timeText,CultureInfo.InvariantCulture,DateTimeStyles.AssumeLocal,out var t)?t:null;
        if(time==null || (now-time.Value).TotalSeconds>10 || time>now.AddMinutes(1)) return [];
        return root.Descendants().Where(x=>x.Element("value")!=null).Select(x=>
            new Metric("export",x.Element("name")?.Value??"",double.TryParse(x.Element("value")?.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var v) && double.IsFinite(v)?v:null,x.Element("unit")?.Value??"","Aquasuite",time)).ToList();
    }
}
