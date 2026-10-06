using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace PcAiDashboard;

internal static class ClaudeCredentials
{
    public static (string Token,string Source)? Find()
    {
        var home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        try
        {
            using var d=JsonDocument.Parse(File.ReadAllText(Path.Combine(home,".claude",".credentials.json")));
            var oauth=d.RootElement.GetProperty("claudeAiOauth");
            if(oauth.GetProperty("expiresAt").GetInt64()>DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds()) return(oauth.GetProperty("accessToken").GetString()!,"Claude Code");
        }catch(Exception) { }
        // Only the dedicated Claude OAuth cache, never browser cookies or unrelated credentials.
        var dirs=new List<string> { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Claude") };
        var packages=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Packages");
        if(Directory.Exists(packages)) dirs.AddRange(Directory.EnumerateDirectories(packages,"Claude_*").Select(d=>Path.Combine(d,"LocalCache","Roaming","Claude")));
        foreach(var dir in dirs)
        {
            try
            {
                using var config=JsonDocument.Parse(File.ReadAllText(Path.Combine(dir,"config.json")));
                using var state=JsonDocument.Parse(File.ReadAllText(Path.Combine(dir,"Local State")));
                var wrapped=Convert.FromBase64String(state.RootElement.GetProperty("os_crypt").GetProperty("encrypted_key").GetString()!);
                if(!wrapped.AsSpan(0,5).SequenceEqual("DPAPI"u8)) continue;
                var key=ProtectedData.Unprotect(wrapped[5..],null,DataProtectionScope.CurrentUser);
                try
                {
                    foreach(var name in new[]{"oauth:tokenCacheV2","oauth:tokenCache"})
                    {
                        if(!config.RootElement.TryGetProperty(name,out var cache) || cache.ValueKind!=JsonValueKind.String) continue;
                        var blob=Convert.FromBase64String(cache.GetString()!);
                        if(blob.Length<31 || !blob.AsSpan(0,3).SequenceEqual("v10"u8)) continue;
                        var plaintext=new byte[blob.Length-31];
                        using var aes=new AesGcm(key,16); aes.Decrypt(blob.AsSpan(3,12),blob.AsSpan(15,blob.Length-31),blob.AsSpan(blob.Length-16),plaintext);
                        try
                        {
                            using var tokens=JsonDocument.Parse(plaintext);
                            foreach(var item in tokens.RootElement.EnumerateObject().OrderByDescending(p=>p.Value.TryGetProperty("expiresAt",out var exp)&&exp.TryGetInt64(out var ms)?ms:0))
                            {
                                if(!item.Name.Contains("user:profile",StringComparison.Ordinal) || !item.Value.TryGetProperty("token",out var token)) continue;
                                if(item.Value.TryGetProperty("expiresAt",out var expiry)&&expiry.TryGetInt64(out var expires) && expires<DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds()) continue;
                                var value=token.GetString(); if(!string.IsNullOrWhiteSpace(value)) return(value,"Claude Desktop");
                            }
                        }
                        finally { CryptographicOperations.ZeroMemory(plaintext); }
                    }
                }
                finally { CryptographicOperations.ZeroMemory(key); }
            }
            catch(Exception) { }
        }
        return null;
    }
}
