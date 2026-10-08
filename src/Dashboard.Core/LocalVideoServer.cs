using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace PcAiDashboard;

// WebView2's managed response stream can crash natively with multi-GB files.
// Loopback HTTP keeps media outside COM and copies only a 64 KiB block at a time.
public sealed class LocalVideoServer : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    // Every monitor can hold a long-running video response plus seek/metadata requests.
    // Keep room for the normal dashboard too; each active response uses only 64 KiB.
    private readonly SemaphoreSlim slots = new(32);
    private readonly string secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    private readonly string origin;
    private readonly string baseUrl;
    private readonly string? webRoot;
    private readonly string webPrefix;
    private static readonly Dictionary<string, string> webAssets = new(StringComparer.Ordinal)
    {
        ["index.html"] = "text/html; charset=utf-8", ["main.js"] = "text/javascript; charset=utf-8",
        ["styles.css"] = "text/css; charset=utf-8", ["dashboard.css"] = "text/css; charset=utf-8",
        ["notebook.css"] = "text/css; charset=utf-8", ["dashboard-icon.svg"] = "image/svg+xml"
    };
    private volatile Media? selected;
    private record Media(string Path, string Route);
    public string Url { get; private set; } = "";

    public string DashboardUrl => webRoot == null ? "" : baseUrl + webPrefix + "index.html";

    public LocalVideoServer(string? origin, string? webRoot = null)
    {
        this.webRoot = webRoot == null ? null : Path.GetFullPath(webRoot);
        webPrefix = $"/{secret}/ui/";
        listener.Start();
        baseUrl = $"http://{(origin == null ? "localhost" : "127.0.0.1")}:{((IPEndPoint)listener.LocalEndpoint).Port}";
        this.origin = origin ?? baseUrl;
        _ = AcceptRequests();
    }

    public void Configure(string path)
    {
        if (selected?.Path == path) return;
        var route = $"/{secret}/{Guid.NewGuid():N}";
        selected = string.IsNullOrWhiteSpace(path) ? null : new(path, route);
        Url = selected == null ? "" : baseUrl + route;
    }

    private async Task AcceptRequests()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await slots.WaitAsync(stop.Token);
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(stop.Token); }
                catch { slots.Release(); throw; }
                _ = HandleClient(client);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }

    private async Task HandleClient(TcpClient client)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        try
        {
            using (client)
            {
                client.NoDelay = true;
                var network = client.GetStream();
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                var header = new MemoryStream();
                var single = new byte[1];
                uint tail = 0;
                while (header.Length < 16384)
                {
                    if (await network.ReadAsync(single, timeout.Token) == 0) return;
                    header.WriteByte(single[0]); tail = (tail << 8) | single[0];
                    if (tail == 0x0d0a0d0a) break;
                }
                if (tail != 0x0d0a0d0a) { await Reply(network, 431, "Request Header Fields Too Large", 0, "", timeout.Token); return; }
                var lines = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n");
                var request = lines[0].Split(' ');
                var media = selected;
                var asset = request.Length == 3 && webRoot != null && request[1].StartsWith(webPrefix, StringComparison.Ordinal)
                    ? request[1][webPrefix.Length..] : "";
                var isAsset = webRoot != null && webAssets.ContainsKey(asset);
                if (request.Length != 3 || (!isAsset && (media == null || request[1] != media.Route)))
                { await Reply(network, 404, "Not Found", 0, "", timeout.Token); return; }
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in lines.Skip(1)) { int colon = line.IndexOf(':'); if (colon > 0) headers[line[..colon]] = line[(colon + 1)..].Trim(); }
                if (headers.TryGetValue("Origin", out var requestOrigin) && requestOrigin != origin)
                { await Reply(network, 403, "Forbidden", 0, "", timeout.Token); return; }
                if (request[0] == "OPTIONS")
                { await Reply(network, 204, "No Content", 0, "Access-Control-Allow-Methods: GET, HEAD, OPTIONS\r\nAccess-Control-Allow-Headers: Range\r\nAccess-Control-Allow-Private-Network: true\r\n", timeout.Token); return; }
                if (request[0] is not ("GET" or "HEAD"))
                { await Reply(network, 405, "Method Not Allowed", 0, "Allow: GET, HEAD, OPTIONS\r\n", timeout.Token); return; }
                var path = isAsset ? Path.Combine(webRoot!, asset) : media!.Path;
                if (!File.Exists(path)) { await Reply(network, 404, "Not Found", 0, "", timeout.Token); return; }
                await using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.Asynchronous);
                var length = file.Length;
                var partial = headers.TryGetValue("Range", out var range);
                if (!TryRange(range, length, out var start, out var end))
                { await Reply(network, 416, "Range Not Satisfiable", 0, $"Content-Range: bytes */{length}\r\n", timeout.Token); return; }
                string mime = isAsset ? webAssets[asset] : Path.GetExtension(path).Equals(".webm", StringComparison.OrdinalIgnoreCase) ? "video/webm" : "video/mp4";
                var extra = $"Content-Type: {mime}\r\nAccept-Ranges: bytes\r\n";
                if (partial) extra += $"Content-Range: bytes {start}-{end}/{length}\r\n";
                await Reply(network, partial ? 206 : 200, partial ? "Partial Content" : "OK", end - start + 1, extra, timeout.Token);
                if (request[0] == "HEAD") return;
                timeout.CancelAfter(Timeout.InfiniteTimeSpan);
                file.Position = start;
                var buffer = new byte[65536];
                long remaining = end - start + 1;
                while (remaining > 0)
                {
                    int read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), timeout.Token);
                    if (read == 0) break;
                    await network.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                    remaining -= read;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException or SocketException or ObjectDisposedException) { }
        catch (Exception e) { AppFiles.Log("Lokales Video: " + e.GetType().Name); }
        finally { client.Dispose(); slots.Release(); }
    }

    private Task Reply(NetworkStream network, int status, string reason, long length, string extra, CancellationToken token)
    {
        var header = $"HTTP/1.1 {status} {reason}\r\nContent-Length: {length}\r\nConnection: close\r\nCache-Control: no-store\r\nAccess-Control-Allow-Origin: {origin}\r\n{extra}\r\n";
        return network.WriteAsync(Encoding.ASCII.GetBytes(header), token).AsTask();
    }

    public static bool TryRange(string? range, long length, out long start, out long end)
    {
        start = 0; end = length - 1;
        if (length <= 0) return false;
        if (range == null) return true;
        if (!range.StartsWith("bytes=", StringComparison.Ordinal)) return false;
        var parts = range[6..].Split('-');
        if (parts.Length != 2) return false;
        bool Parse(string s, out long value) => long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        if (parts[0].Length == 0)
        {
            if (!Parse(parts[1], out var suffix) || suffix <= 0) return false;
            start = Math.Max(0, length - suffix);
        }
        else
        {
            if (!Parse(parts[0], out start) || start >= length) return false;
            if (parts[1].Length > 0)
            {
                if (!Parse(parts[1], out var requestedEnd)) return false;
                end = Math.Min(end, requestedEnd);
            }
        }
        return start <= end;
    }

    public void Dispose() { stop.Cancel(); listener.Stop(); }
}
