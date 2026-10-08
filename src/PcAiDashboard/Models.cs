using System.IO;
using System.Text.Json;

namespace PcAiDashboard;

public record Metric(string Id, string Label, double? Value, string Unit, string Source, DateTimeOffset? UpdatedAt = null);
public record DriveUsage(string Name, string Label, double UsedGb, double TotalGb, double UsedPercent);
public record Quota(string Label, double UsedPercent, long? ResetsAt, int? WindowMinutes = null);
public record SessionUsage(string Id, string Label, double? UsedPercent, long? Tokens, long? Capacity, DateTimeOffset UpdatedAt);
public record CreditUsage(string Status, double? Balance = null, string Unit = "Credits", bool Unlimited = false,
    double? Spent = null, double? Limit = null, bool Enabled = true, string? Detail = null);
public record ManualResetUsage(long AvailableCount, long? NextExpiresAt = null);
public record AiUsage(string Name, string Status, List<Quota> Quotas, List<SessionUsage> Sessions, DateTimeOffset? UpdatedAt = null, string? Detail = null, CreditUsage? Credits = null, ManualResetUsage? ManualResets = null);
public record DisplayInfo(string Id, string Label, int Width, int Height, int X, int Y, bool Primary);
public record DashboardSnapshot(DateTimeOffset Time, List<Metric> Metrics, List<DriveUsage> Drives, List<AiUsage> Ai, string HardwareStatus);

public sealed class DashboardSettings
{
    public string DisplayId { get; set; } = "";
    public bool Fullscreen { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = true;
    public bool Autostart { get; set; }
    public bool ScreenSaverTimerEnabled { get; set; }
    public int ScreenSaverIdleMinutes { get; set; } = 10;
    // The saver always covers all monitors. Null shows dashboard cards everywhere;
    // an explicit empty list shows only the background on every monitor.
    public List<string>? ScreenSaverDashboardDisplayIds { get; set; }
    public bool StartAquasuite { get; set; } = true;
    public string BackgroundMode { get; set; } = "gradient";
    public string YoutubeUrl { get; set; } = "";
    public string LocalVideoPath { get; set; } = "";
    public bool Muted { get; set; } = true;
    public int Volume { get; set; } = 25;
    public double Dim { get; set; } = 0.5;
    public double CardOpacity { get; set; } = 0.76;
    public string Accent { get; set; } = "#66e7c8";
    public string TextColor { get; set; } = "#ecf3f6";
    public int TemperatureScaleMax { get; set; } = 100;
    public string AquasuiteSharedMemory { get; set; } = "PC-AI-Dashboard";
    public string AquasuiteXmlPath { get; set; } = "";
    public int PumpChannel { get; set; } = 4;
    public int SideChannel { get; set; } = 0;
    public int BottomChannel { get; set; } = 1;
    public int BackChannel { get; set; } = 2;
    public int TopChannel { get; set; } = 3;
    public int PumpTempChannel { get; set; }
    public int RadiatorTempChannel { get; set; } = 2;
    public int CaseTempChannel { get; set; } = 3;
    public string CodexSessionId { get; set; } = "";
    public string ClaudeSessionId { get; set; } = "";
    public static DashboardSettings Validate(DashboardSettings s)
    {
        s.Volume = Math.Clamp(s.Volume, 0, 100);
        s.ScreenSaverIdleMinutes = Math.Clamp(s.ScreenSaverIdleMinutes, 1, 240);
        s.ScreenSaverDashboardDisplayIds = s.ScreenSaverDashboardDisplayIds?.Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(64).ToList();
        s.Dim = Math.Clamp(double.IsFinite(s.Dim) ? s.Dim : .5, 0, .95);
        s.CardOpacity = Math.Clamp(double.IsFinite(s.CardOpacity) ? s.CardOpacity : .76, .1, 1);
        s.TemperatureScaleMax = Math.Clamp(s.TemperatureScaleMax,40,120);
        if (!System.Text.RegularExpressions.Regex.IsMatch(s.Accent ?? "", "^#[0-9a-fA-F]{6}$")) s.Accent = "#66e7c8";
        if (!System.Text.RegularExpressions.Regex.IsMatch(s.TextColor ?? "", "^#[0-9a-fA-F]{6}$")) s.TextColor = "#ecf3f6";
        if (s.BackgroundMode is not ("gradient" or "youtube" or "local")) s.BackgroundMode = "gradient";
        s.PumpChannel = Math.Clamp(s.PumpChannel,0,7); s.SideChannel = Math.Clamp(s.SideChannel,0,7);
        s.BottomChannel = Math.Clamp(s.BottomChannel,0,7); s.BackChannel = Math.Clamp(s.BackChannel,0,7); s.TopChannel = Math.Clamp(s.TopChannel,0,7);
        s.PumpTempChannel = Math.Clamp(s.PumpTempChannel,0,3); s.RadiatorTempChannel = Math.Clamp(s.RadiatorTempChannel,0,3); s.CaseTempChannel = Math.Clamp(s.CaseTempChannel,0,3);
        return s;
    }
}

public static class AppFiles
{
    public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PcAiDashboard");
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static void Log(string message)
    {
        try { Directory.CreateDirectory(Root); var p=Path.Combine(Root,"app.log"); if(File.Exists(p) && new FileInfo(p).Length > 2_000_000) File.Move(p,p+".previous",true); File.AppendAllText(p,$"{DateTimeOffset.Now:O} {message}{Environment.NewLine}"); } catch { }
    }
    public static DashboardSettings LoadSettings()
    {
        Directory.CreateDirectory(Root);
        try { return DashboardSettings.Validate(JsonSerializer.Deserialize<DashboardSettings>(File.ReadAllText(Path.Combine(Root,"settings.json")),Json) ?? new()); }
        catch { return new(); }
    }
    public static void SaveSettings(DashboardSettings s)
    {
        Directory.CreateDirectory(Root);
        var p=Path.Combine(Root,"settings.json"); File.WriteAllText(p+".tmp",JsonSerializer.Serialize(DashboardSettings.Validate(s),Json)); File.Move(p+".tmp",p,true);
    }
}
