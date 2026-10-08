using System.Text.Json;
using PcAiDashboard;

// A private stdio child of the native app. The loopback media server exposes
// only bundled UI assets, never provider RPC or account mutation commands.
if (!OperatingSystem.IsMacOS()) return;
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
var verificationIndex = Array.IndexOf(args, "--verify-media");
var verification = verificationIndex >= 0;
if (verification && verificationIndex + 1 >= args.Length) return;
var defaults = new DashboardSettings { Profile = "notebook", Fullscreen = false, AlwaysOnTop = false, StartAquasuite = false };
var settings = verification ? defaults : AppFiles.LoadSettings(defaults);
if (verification) { settings.LocalVideoPath = args[verificationIndex + 1]; settings.BackgroundMode = "local"; settings.Muted = true; }
await using var ai = verification ? null : new AiService(new MacAiPlatform(), !args.Contains("--no-sessions"));
using var web = new LocalVideoServer(null, Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Web")));
var outputGate = new SemaphoreSlim(1);
var compactJson = new JsonSerializerOptions(AppFiles.Json) { WriteIndented = false };
async Task Send(object message)
{
    var line = JsonSerializer.Serialize(message, compactJson);
    await outputGate.WaitAsync(stop.Token);
    try { await Console.Out.WriteLineAsync(line); await Console.Out.FlushAsync(); }
    finally { outputGate.Release(); }
}
async Task Configure()
{
    await Send(new { type = "configuration", settings, mediaUrl = "", dashboardUrl = web.DashboardUrl });
}
var publisher = Task.Run(async () =>
{
    try
    {
        string? previous = null;
        while (!stop.IsCancellationRequested)
        {
            if (ai == null) return;
            var providers = ai.Read();
            var current = JsonSerializer.Serialize(providers, compactJson);
            if (current != previous) { await Send(new { type = "ai", data = providers }); previous = current; }
            await Task.Delay(1000, stop.Token);
        }
    }
    catch (OperationCanceledException) { }
});
await Configure();
try
{
    while (await Console.In.ReadLineAsync(stop.Token) is { } line)
    {
        if (line.Length > 64_000) continue;
        try
        {
            using var doc = JsonDocument.Parse(line);
            if (!doc.RootElement.TryGetProperty("type", out var type)) continue;
            switch (type.GetString())
            {
                case "ready": case "refresh": await Configure(); break;
                case "saveSettings":
                    if (verification) break;
                    settings = DashboardSettings.Validate(doc.RootElement.GetProperty("settings").Deserialize<DashboardSettings>(AppFiles.Json) ?? settings);
                    settings.StartAquasuite = false; settings.ScreenSaverTimerEnabled = false; settings.Autostart = false;
                    AppFiles.SaveSettings(settings); await Configure(); break;
                case "installClaudeIntegration":
                    if (verification) break;
                    try
                    {
                        var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Web", "integrations", "claude-statusline.cjs"));
                        await Send(new { type = "notice", message = ClaudeStatusLine.Install(source) });
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
                    { await Send(new { type = "notice", message = "Claude-Kontextanbindung konnte nicht eingerichtet werden." }); }
                    break;
                case "sleep": ai?.Dispose(); stop.Cancel(); break;
                // Everything else, including a reset or provider RPC, is ignored.
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException) { }
        if (stop.IsCancellationRequested) break;
    }
}
catch (OperationCanceledException) { }
finally { stop.Cancel(); await publisher; }
