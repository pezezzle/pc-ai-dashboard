using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using HidSharp;

namespace PcAiDashboard;

public sealed class HardwareService : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly object gate = new();
    private byte[]? octo;
    private DateTimeOffset octoTime;
    private double? gpuTemp, gpuLoad;
    private DateTimeOffset gpuTime;
    private ulong lastIdle, lastKernel, lastUser;
    private string octoStatus="OCTO wird verbunden";
    public HardwareService() { _=Task.Run(ReadOcto); _=Task.Run(ReadNvidia); }

    private async Task ReadOcto()
    {
        while(!stop.IsCancellationRequested)
        {
            try
            {
                var device=DeviceList.Local.GetHidDevices(0x0c70,0xf011).FirstOrDefault() ?? throw new IOException("OCTO nicht gefunden");
                using var stream=device.Open(); stream.ReadTimeout=2500;
                var data=new byte[device.GetMaxInputReportLength()];
                while(!stop.IsCancellationRequested)
                {
                    var n=stream.Read(data);
                    if(n>=327 && data[0]==1) lock(gate) { octo=data.ToArray(); octoTime=DateTimeOffset.Now; octoStatus="OCTO live"; }
                }
            }
            catch(Exception e) when(!stop.IsCancellationRequested) { lock(gate) octoStatus=e is TimeoutException ? "OCTO wartet auf Daten" : "OCTO nicht erreichbar"; }
            try { await Task.Delay(3000,stop.Token); } catch(OperationCanceledException) { break; }
        }
    }

    private async Task ReadNvidia()
    {
        while(!stop.IsCancellationRequested)
        {
            try
            {
                using var p=new Process { StartInfo=new("nvidia-smi.exe") { UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true } };
                p.StartInfo.ArgumentList.Add("--query-gpu=temperature.gpu,utilization.gpu"); p.StartInfo.ArgumentList.Add("--format=csv,noheader,nounits");
                p.Start(); using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(6000);
                try { var output=await p.StandardOutput.ReadToEndAsync(timeout.Token); await p.WaitForExitAsync(timeout.Token); var row=output.Split('\n')[0].Split(',');
                    if(row.Length>=2 && double.TryParse(row[0],CultureInfo.InvariantCulture,out var t) && double.TryParse(row[1],CultureInfo.InvariantCulture,out var l)) lock(gate) { gpuTemp=t; gpuLoad=l; gpuTime=DateTimeOffset.Now; }
                } finally { if(!p.HasExited) p.Kill(true); }
            }
            catch(Exception) { }
            try { await Task.Delay(2000,stop.Token); } catch(OperationCanceledException) { break; }
        }
    }

    public (List<Metric> Metrics,List<DriveUsage> Drives,string Status) Read(DashboardSettings settings)
    {
        var now=DateTimeOffset.Now;
        var metrics=new List<Metric>();
        byte[]? report; DateTimeOffset at; string status;
        lock(gate) { report=octo?.ToArray(); at=octoTime; status=octoStatus; }
        bool fresh=report!=null && (now-at).TotalSeconds<6;
        void Temp(string id,string label,int channel) => metrics.Add(new(id,label,fresh?OctoTemperature(report!,channel):null,"°C","OCTO",fresh?at:null));
        void Fan(string id,string label,int channel) => metrics.Add(new(id,label,fresh?OctoRpm(report!,channel):null,"RPM","OCTO",fresh?at:null));
        Temp("coolantPump","Pump Coolant",settings.PumpTempChannel); Temp("coolantRadiator","Radiator Coolant",settings.RadiatorTempChannel); Temp("caseTemp","Case",settings.CaseTempChannel);
        Fan("pumpRpm","Pumpe",settings.PumpChannel); Fan("topRpm","Top",settings.TopChannel); Fan("sideRpm","Side",settings.SideChannel); Fan("bottomRpm","Bottom",settings.BottomChannel); Fan("backRpm","Back",settings.BackChannel);
        var export=ReadExport(settings);
        metrics.Add(new("cpuTemp","CPU Package",export.FirstOrDefault(x=>x.Label.Equals("CPU Package",StringComparison.OrdinalIgnoreCase) && x.Unit.Contains('C'))?.Value,"°C","Aquasuite",export.FirstOrDefault(x=>x.Label=="CPU Package")?.UpdatedAt));
        double? cpu=null;
        if(GetSystemTimes(out var idle,out var kernel,out var user))
        {
            ulong i=idle.Value,k=kernel.Value,u=user.Value; var total=(k-lastKernel)+(u-lastUser);
            if(lastKernel!=0 && total>0) cpu=Math.Clamp(100.0*(1.0-(double)(i-lastIdle)/total),0,100);
            lastIdle=i; lastKernel=k; lastUser=u;
        }
        metrics.Add(new("cpuLoad","CPU Total",cpu,"%","Windows",now));
        lock(gate) { bool gf=(now-gpuTime).TotalSeconds<10; metrics.Add(new("gpuTemp","GPU Core",gf?gpuTemp:null,"°C","NVIDIA",gf?gpuTime:null)); metrics.Add(new("gpuLoad","GPU Usage",gf?gpuLoad:null,"%","NVIDIA",gf?gpuTime:null)); }
        var mem=new MemoryStatus();
        if(GlobalMemoryStatusEx(mem)) { double total=mem.TotalPhysical/1073741824.0,used=(mem.TotalPhysical-mem.AvailablePhysical)/1073741824.0; metrics.Add(new("ramUsed","RAM",used,"GB","Windows",now)); metrics.Add(new("ramTotal","RAM Total",total,"GB","Windows",now)); metrics.Add(new("ramLoad","RAM Usage",100*used/total,"%","Windows",now)); }
        // An explicit export is authoritative and can also provide GPU metrics or a different controller.
        foreach(var e in export) { var match=metrics.FindIndex(m=>Normalize(m.Label)==Normalize(e.Label) && (m.Unit==e.Unit || m.Unit=="RPM" && e.Unit.Equals("rpm",StringComparison.OrdinalIgnoreCase))); if(match>=0) metrics[match]=e with { Id=metrics[match].Id }; }
        var drives=new List<DriveUsage>();
        foreach(var drive in DriveInfo.GetDrives()) try { if(drive.DriveType==DriveType.Fixed && drive.IsReady && drive.TotalSize>0) drives.Add(new(drive.Name,drive.VolumeLabel,(drive.TotalSize-drive.TotalFreeSpace)/1073741824.0,drive.TotalSize/1073741824.0,100.0*(drive.TotalSize-drive.TotalFreeSpace)/drive.TotalSize)); }catch(IOException) { }
        return(metrics,drives,fresh?"OCTO live · Aquasuite "+(metrics.First(m=>m.Id=="cpuTemp").Value.HasValue?"live":"ohne CPU-Daten"):status);
    }
    private static string Normalize(string s)=>s.ToLowerInvariant().Replace(" ","");
    public static double? OctoTemperature(byte[] report,int channel)
    {
        int index=0x3d+2*channel; if(channel is <0 or >3 || report.Length<index+2) return null; int v=report[index]*256+report[index+1]; return v==0x7fff?null:v/100.0;
    }
    public static double? OctoRpm(byte[] report,int channel)
    {
        int index=0x85+13*channel; if(channel is <0 or >7 || report.Length<index+2) return null; return report[index]*256+report[index+1];
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
    private List<Metric> ReadExport(DashboardSettings settings)
    {
        try
        {
            string xml;
            if(!string.IsNullOrWhiteSpace(settings.AquasuiteXmlPath)) { using var f=new FileStream(settings.AquasuiteXmlPath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete); using var r=new StreamReader(f); xml=r.ReadToEnd(); }
            else { using var m=MemoryMappedFile.OpenExisting(settings.AquasuiteSharedMemory,MemoryMappedFileRights.Read); using var v=m.CreateViewStream(0,0,MemoryMappedFileAccess.Read); if(v.Length>4_000_000) return []; var b=new byte[v.Length]; v.ReadExactly(b); xml=Encoding.UTF8.GetString(b).TrimEnd('\0'); }
            return ParseExport(xml,DateTimeOffset.Now);
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Xml.XmlException or ArgumentException) { return []; }
    }
    public void Dispose()=>stop.Cancel();
    [StructLayout(LayoutKind.Sequential)] private struct FileTime { public uint Low,High; public readonly ulong Value=>((ulong)High<<32)|Low; }
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out FileTime idle,out FileTime kernel,out FileTime user);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Auto)] private class MemoryStatus { public uint Length=(uint)Marshal.SizeOf<MemoryStatus>(); public uint Load; public ulong TotalPhysical,AvailablePhysical,TotalPageFile,AvailablePageFile,TotalVirtual,AvailableVirtual,AvailableExtended; }
    [DllImport("kernel32.dll",CharSet=CharSet.Auto)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalMemoryStatusEx([In,Out] MemoryStatus status);
}
