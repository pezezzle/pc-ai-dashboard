using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PcAiDashboard;

// Claude Desktop's dedicated OAuth cache, using Chromium's macOS v10 OSCrypt
// format (PBKDF2-SHA1/AES-128-CBC). This never reads browser cookies or writes a
// credential. The native adapter supplies the existing Keychain secret in memory.
public static class MacDesktopCredentials
{
    public static ClaudeCredential? Decode(string config, string password, DateTimeOffset now)
    {
        byte[]? key = null;
        var secret = Encoding.UTF8.GetBytes(password);
        try
        {
            using var json = JsonDocument.Parse(config);
            key = Rfc2898DeriveBytes.Pbkdf2(secret, "saltysalt"u8, 1003, HashAlgorithmName.SHA1, 16);
            foreach (var name in new[] { "oauth:tokenCacheV2", "oauth:tokenCache" })
            {
                if (!json.RootElement.TryGetProperty(name, out var cache) || cache.ValueKind != JsonValueKind.String) continue;
                byte[]? plaintext = null;
                try
                {
                    var blob = Convert.FromBase64String(cache.GetString()!);
                    if (blob.Length <= 3 || (blob.Length - 3) % 16 != 0 || !blob.AsSpan(0, 3).SequenceEqual("v10"u8)) continue;
                    using var aes = Aes.Create(); aes.Key = key;
                    plaintext = aes.DecryptCbc(blob.AsSpan(3), Enumerable.Repeat((byte)' ',16).ToArray(), PaddingMode.PKCS7);
                    using var tokens = JsonDocument.Parse(plaintext);
                    foreach (var entry in tokens.RootElement.EnumerateObject().OrderByDescending(p => p.Value.TryGetProperty("expiresAt",out var expiry) && expiry.TryGetInt64(out var ms) ? ms : 0))
                    {
                        if (!entry.Name.Contains("user:profile", StringComparison.Ordinal) || entry.Value.ValueKind != JsonValueKind.Object ||
                            !entry.Value.TryGetProperty("token", out var token) || token.ValueKind != JsonValueKind.String) continue;
                        if (entry.Value.TryGetProperty("expiresAt",out var expiry) && expiry.TryGetInt64(out var ms) && ms <= now.AddMinutes(1).ToUnixTimeMilliseconds()) continue;
                        var value = token.GetString();
                        if (!string.IsNullOrWhiteSpace(value)) return new(value, "Claude Desktop");
                    }
                }
                catch (Exception e) when (e is JsonException or FormatException or CryptographicException or InvalidOperationException) { }
                finally { if (plaintext != null) CryptographicOperations.ZeroMemory(plaintext); }
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { }
        finally { CryptographicOperations.ZeroMemory(secret); if (key != null) CryptographicOperations.ZeroMemory(key); }
        return null;
    }
}
