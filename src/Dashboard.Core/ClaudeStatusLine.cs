using System.Text.Json.Nodes;

namespace PcAiDashboard;

public static class ClaudeStatusLine
{
    public static string Install(string source)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folder = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? Path.Combine(home, ".claude");
        if (!Directory.Exists(folder)) return "Claude Code zuerst starten und anmelden.";
        var file = Path.Combine(folder, "settings.json");
        var json = File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file))?.AsObject() ?? new JsonObject() : new JsonObject();
        if (json["statusLine"] != null) return "Bestehende Claude-Statuszeile wurde erhalten. Für Kontextdaten muss sie den Dashboard-Collector zusätzlich aufrufen.";
        Directory.CreateDirectory(AppFiles.Root);
        var target = Path.Combine(AppFiles.Root, "claude-statusline.cjs");
        File.Copy(source, target, true);
        var command = OperatingSystem.IsWindows() ? "node \"" + target.Replace('\\', '/') + "\"" : "node '" + target.Replace("'", "'\"'\"'") + "'";
        json["statusLine"] = new JsonObject { ["type"] = "command", ["command"] = command };
        if (File.Exists(file)) File.Copy(file, Path.Combine(AppFiles.Root, "claude-settings.before-dashboard." + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + ".json"));
        File.WriteAllText(file + ".dashboard.tmp", json.ToJsonString(AppFiles.Json));
        File.Move(file + ".dashboard.tmp", file, true);
        return "Claude-Code-Kontext verbunden. Eine neue Sitzung liefert Messwerte.";
    }
}
