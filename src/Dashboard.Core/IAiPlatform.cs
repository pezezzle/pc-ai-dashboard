namespace PcAiDashboard;

public record ClaudeCredential(string Token, string Source);

// Native discovery and credential access stay outside the portable polling/parsing code.
public interface IAiPlatform
{
    string? FindCodex();
    Task<ClaudeCredential?> FindClaudeCredentials(CancellationToken token);
}
