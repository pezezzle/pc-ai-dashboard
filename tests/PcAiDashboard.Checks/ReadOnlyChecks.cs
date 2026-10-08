using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using PcAiDashboard;

static class ReadOnlyChecks
{
    public static async Task Run(Action<bool,string> assert)
    {
        CheckDesktopCache(assert);
        var allowed = new[] { CodexReadOnlyProtocol.Request("initialize",1), CodexReadOnlyProtocol.Request("initialized"), CodexReadOnlyProtocol.Request("account/rateLimits/read",2) };
        assert(allowed.Select(line => JsonDocument.Parse(line).RootElement.GetProperty("method").GetString())
            .SequenceEqual(new[] { "initialize", "initialized", "account/rateLimits/read" }), "Outbound protocol contains only handshake and account-limit reading");
        foreach(var method in new[] { "account/rateLimitResetCredit/consume", "account/login/start", "turn/start", "account/logout", "arbitrary" })
        {
            bool rejected = false;
            try { CodexReadOnlyProtocol.Request(method,3); } catch (InvalidOperationException) { rejected = true; }
            assert(rejected, "Read-only protocol rejects " + method);
        }
        var path = Path.GetTempFileName();
        var previous = Environment.GetEnvironmentVariable("DASHBOARD_TEST_REQUEST_LOG");
        try
        {
            Environment.SetEnvironmentVariable("DASHBOARD_TEST_REQUEST_LOG", path);
            using var service = new AiService(new FakePlatform(), readSessions:false);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (service.Read()[0].Status != "Live" && DateTime.UtcNow < deadline) await Task.Delay(50);
            var usage = service.Read()[0];
            assert(usage.Status == "Live" && usage.ManualResets?.AvailableCount == 1, "Real polling against a synthetic app-server reads manual-reset availability");
            var methods = File.ReadAllLines(path);
            assert(methods.SequenceEqual(new[] { "initialize", "initialized", "account/rateLimits/read" }), "Polling sends no reset-redemption or inference request");
            assert(!JsonSerializer.Serialize(usage,AppFiles.Json).Contains("opaque-credit-test"), "Opaque reset IDs never leave the parser or reach a UI snapshot");
        }
        finally { Environment.SetEnvironmentVariable("DASHBOARD_TEST_REQUEST_LOG", previous); File.Delete(path); }
    }

    private static void CheckDesktopCache(Action<bool,string> assert)
    {
        var now = DateTimeOffset.UtcNow;
        const string password = "synthetic-safe-storage-secret";
        var expiry = now.AddHours(1).ToUnixTimeMilliseconds();
        string Encrypt(string payload)
        {
            using var aes = Aes.Create();
            aes.Key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), "saltysalt"u8, 1003, HashAlgorithmName.SHA1,16);
            return Convert.ToBase64String("v10"u8.ToArray().Concat(aes.EncryptCbc(Encoding.UTF8.GetBytes(payload), Enumerable.Repeat((byte)' ',16).ToArray())).ToArray());
        }
        var payload = JsonSerializer.Serialize(new Dictionary<string,object> {
            ["install:user:inference"] = new { token = "inference-only-test", expiresAt = expiry + 1000 },
            ["install:user:profile user:inference"] = new { token = "profile-test", expiresAt = expiry }
        });
        var config = JsonSerializer.Serialize(new Dictionary<string,string> { ["oauth:tokenCacheV2"] = Encrypt(payload) });
        assert(MacDesktopCredentials.Decode(config,password,now)==new ClaudeCredential("profile-test","Claude Desktop"), "Mac Desktop cache decrypts only profile-scoped usage credentials");
        assert(MacDesktopCredentials.Decode(config,"wrong-secret",now)==null, "Wrong Keychain secret cannot yield a Desktop credential");
        assert(MacDesktopCredentials.Decode(config,password,now.AddHours(2))==null, "Expired Desktop credentials are unavailable");
        assert(MacDesktopCredentials.Decode("{}",password,now)==null && MacDesktopCredentials.Decode("bad",password,now)==null, "Missing and malformed Desktop caches stay unavailable");
    }

    public static int FakeServer()
    {
        while(Console.ReadLine() is { } line)
        {
            using var request = JsonDocument.Parse(line);
            var method = request.RootElement.GetProperty("method").GetString()!;
            File.AppendAllText(Environment.GetEnvironmentVariable("DASHBOARD_TEST_REQUEST_LOG")!, method + Environment.NewLine);
            if (method == "initialize") Console.WriteLine("{\"id\":1,\"result\":{}}");
            else if (method == "account/rateLimits/read")
            {
                Console.WriteLine("""{"id":2,"result":{"rateLimits":{"primary":{"usedPercent":34,"windowDurationMins":300}},"rateLimitResetCredits":{"availableCount":1,"credits":[{"id":"opaque-credit-test","status":"available","resetType":"codexRateLimits"}]}}}""");
            }
            else if (method != "initialized") return 9;
        }
        return 0;
    }
    private sealed class FakePlatform : IAiPlatform
    {
        public string? FindCodex() => Environment.ProcessPath;
        public Task<ClaudeCredential?> FindClaudeCredentials(CancellationToken token) => Task.FromResult<ClaudeCredential?>(null);
    }
}
