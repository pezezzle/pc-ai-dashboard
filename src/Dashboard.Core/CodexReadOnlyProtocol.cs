using System.Text.Json;

namespace PcAiDashboard;

// This dashboard must NEVER redeem a manual reset. This is the complete outbound
// RPC allow-list, including the handshake. No native/web command can extend it.
public static class CodexReadOnlyProtocol
{
    public static string Request(string method, int? id = null)
    {
        object message = method switch
        {
            "initialize" when id.HasValue => new { id, method, @params = new { clientInfo = new { name = "pc_ai_dashboard", version = "1.1.0" } } },
            "initialized" when !id.HasValue => new { method, @params = new { } },
            "account/rateLimits/read" when id.HasValue => new { id, method },
            _ => throw new InvalidOperationException("Dashboard RPC is read-only; this method is forbidden.")
        };
        return JsonSerializer.Serialize(message);
    }
}
