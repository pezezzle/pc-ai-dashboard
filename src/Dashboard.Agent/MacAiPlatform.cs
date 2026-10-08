using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PcAiDashboard;

internal sealed class MacAiPlatform : IAiPlatform
{
    public string? FindCodex()
    {
        // Prefer the installed desktop client's native binary, which can expose
        // manual-reset metadata missing from older CLI versions. Never upgrade it.
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] bundles = ["/Applications/Codex.app", "/Applications/ChatGPT.app", Path.Combine(home, "Applications/Codex.app"), Path.Combine(home, "Applications/ChatGPT.app")];
        foreach (var bundle in bundles)
            foreach (var suffix in new[] { "Contents/Resources/codex", "Contents/Resources/codex-cli/bin/codex", "Contents/Resources/codex-cli/CodexCLI.app/Contents/MacOS/codex" })
            {
                var path = Path.Combine(bundle, suffix);
                if (File.Exists(path)) return path;
            }
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Concat(new[] { "/opt/homebrew/bin", "/usr/local/bin", Path.Combine(home, ".local/bin") }).Where(d => !string.IsNullOrWhiteSpace(d)))
        {
            var path = Path.Combine(dir, "codex");
            if (File.Exists(path)) return path;
        }
        return null;
    }

    public async Task<ClaudeCredential?> FindClaudeCredentials(CancellationToken token)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var configured = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var folder = string.IsNullOrWhiteSpace(configured) ? Path.Combine(home, ".claude") : Path.GetFullPath(configured);
        // Claude Code namespaces non-default configuration directories in Keychain.
        var service = "Claude Code-credentials";
        if (!string.IsNullOrWhiteSpace(configured))
            service += "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(folder))).ToLowerInvariant()[..8];
        var stored = await ReadKeychain(service, token);
        if (stored != null && Parse(stored) is { } credential) return credential;
        try
        {
            if (Parse(await File.ReadAllTextAsync(Path.Combine(folder, ".credentials.json"), token)) is { } fallback) return fallback;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        var desktop = Path.Combine(home, "Library", "Application Support", "Claude", "config.json");
        if (!File.Exists(desktop)) return null;
        var password = await ReadKeychain("Claude Safe Storage", token);
        if (password == null) return null;
        try { return MacDesktopCredentials.Decode(await File.ReadAllTextAsync(desktop, token), password.TrimEnd('\r', '\n'), DateTimeOffset.UtcNow); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    private static async Task<string?> ReadKeychain(string service, CancellationToken token)
    {
        using var process = new Process { StartInfo = new("/usr/bin/security") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (var arg in new[] { "find-generic-password", "-s", service, "-w" }) process.StartInfo.ArgumentList.Add(arg);
        try
        {
            process.Start();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(10));
            var errors = process.StandardError.ReadToEndAsync(deadline.Token);
            try
            {
                var value = await process.StandardOutput.ReadToEndAsync(deadline.Token);
                await process.WaitForExitAsync(deadline.Token); await errors;
                return process.ExitCode == 0 ? value : null;
            }
            finally { if (!process.HasExited) process.Kill(true); }
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception) { }
        token.ThrowIfCancellationRequested();
        return null;
    }

    private static ClaudeCredential? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth) ||
                !oauth.TryGetProperty("accessToken", out var access) || access.ValueKind != JsonValueKind.String ||
                !oauth.TryGetProperty("expiresAt", out var expiry) || !expiry.TryGetInt64(out var ms) ||
                ms <= DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds()) return null;
            var value = access.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : new(value, "Claude Code");
        }
        catch (JsonException) { return null; }
    }
}
