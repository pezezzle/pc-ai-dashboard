using System.IO;
using Microsoft.Win32;

namespace PcAiDashboard;

public sealed class WindowsAiPlatform : IAiPlatform
{
    public Task<ClaudeCredential?> FindClaudeCredentials(CancellationToken token)
    {
        var credential = ClaudeCredentials.Find();
        return Task.FromResult(credential is { } c ? new ClaudeCredential(c.Token, c.Source) : null);
    }

    public string? FindCodex()
    {
        try
        {
            using var packages = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
            foreach (var name in (packages?.GetSubKeyNames() ?? []).Where(n => n.StartsWith("OpenAI.Codex_", StringComparison.Ordinal))
                .OrderByDescending(n => Version.TryParse(n.Split('_').ElementAtOrDefault(1), out var v) ? v : new Version()))
            {
                using var package = packages!.OpenSubKey(name);
                if (package?.GetValue("PackageRootFolder") is not string folder) continue;
                var bundled = Path.Combine(folder, "app", "resources", "codex.exe");
                if (File.Exists(bundled)) return bundled;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        var npm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@openai");
        if (Directory.Exists(npm))
        {
            var exe = Directory.EnumerateFiles(npm, "codex.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (exe != null) return exe;
        }
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(dir, "codex.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
