// @module: reEngine.Core.Updates.UpdatePackageApplier
// @purpose: SSRF URL filter, resumable HTTP Range .part/.part.meta.json downloader, SHA-256 verification, and atomic .staging-<ver> -> v<Ver> + .ready apply.
// @depends-on: System, System.IO, System.IO.Compression, System.Net, System.Net.Http, System.Security.Cryptography, System.Text, System.Text.Json, System.Threading, System.Threading.Tasks, Foundation.Core
// @exports: UpdatePackageApplier

using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundation.Core;

namespace reEngine.Core.Updates;

/// <summary>
/// Downloads engine updates with SSRF protection, resumable <c>.part</c> checkpoints, and atomic <c>.staging-&lt;ver&gt;</c> promotion.
/// </summary>
public sealed class UpdatePackageApplier
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    /// <summary>Blocks non-HTTP/HTTPS schemes and private/link-local SSRF targets unless loopback is explicitly permitted for local testing.</summary>
    public static void AssertSafeRemoteUrl(Uri uri, bool allowLoopback = false)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"SSRF Guard: blocked unsupported URI scheme '{uri.Scheme}'.");
        }

        string host = uri.DnsSafeHost.Trim().Trim('[', ']');
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException("SSRF Guard: remote URI is missing a valid host.");
        }

        if (IPAddress.TryParse(host, out IPAddress? ip))
        {
            AssertSafeIpAddress(ip, allowLoopback);
            return;
        }

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            if (!allowLoopback || !host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"SSRF Guard: blocked private/local hostname '{host}'.");
            }
            return;
        }

        if (host.Equals("metadata.google.internal", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"SSRF Guard: blocked metadata hostname '{host}'.");
        }
    }

    /// <summary>Downloads or resumes an HTTP Range <c>.part</c> payload with <c>.part.meta.json</c> checkpointing and SHA-256 verification.</summary>
    public static async Task<string> DownloadResumablePartAsync(
        Uri sourceUri,
        string partFilePath,
        UpdatePartMeta seedMeta,
        HttpMessageHandler? customHandler = null,
        bool allowLoopback = false,
        TimeSpan? downloadTimeout = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sourceUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(partFilePath);
        ArgumentNullException.ThrowIfNull(seedMeta);

        AssertSafeRemoteUrl(sourceUri, allowLoopback);
        string fullPart = Path.GetFullPath(partFilePath);
        string metaPath = fullPart + ".meta.json";
        Directory.CreateDirectory(Path.GetDirectoryName(fullPart)!);

        long existingBytes = 0;
        if (File.Exists(fullPart) && File.Exists(metaPath))
        {
            UpdatePartMeta? existingMeta = TryReadMeta(metaPath);
            if (existingMeta is not null &&
                string.Equals(existingMeta.DownloadUrl, sourceUri.ToString(), StringComparison.Ordinal) &&
                string.Equals(existingMeta.Sha256Hex, seedMeta.Sha256Hex, StringComparison.OrdinalIgnoreCase))
            {
                existingBytes = new FileInfo(fullPart).Length;
            }
            else
            {
                File.Delete(fullPart);
            }
        }

        using HttpClient client = CreateClient(customHandler, downloadTimeout ?? TimeSpan.FromSeconds(300));
        using var req = new HttpRequestMessage(HttpMethod.Get, sourceUri);
        if (existingBytes > 0) req.Headers.Range = new RangeHeaderValue(existingBytes, null);

        using HttpResponseMessage resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && existingBytes > 0)
        {
            File.Delete(fullPart);
            existingBytes = 0;
            using var retryReq = new HttpRequestMessage(HttpMethod.Get, sourceUri);
            using HttpResponseMessage retryResp = await client.SendAsync(retryReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            retryResp.EnsureSuccessStatusCode();
            await StreamAndVerifyAsync(retryResp, fullPart, metaPath, seedMeta, append: false, 0, ct).ConfigureAwait(false);
            return fullPart;
        }

        resp.EnsureSuccessStatusCode();
        bool append = resp.StatusCode == HttpStatusCode.PartialContent && existingBytes > 0;
        if (!append) existingBytes = 0;
        await StreamAndVerifyAsync(resp, fullPart, metaPath, seedMeta, append, existingBytes, ct).ConfigureAwait(false);
        return fullPart;
    }

    /// <summary>Safely unpacks a verified update ZIP into <c>.staging-&lt;ver&gt;</c>, promotes it to <c>v&lt;Ver&gt;</c>, and writes a <c>.ready</c> marker.</summary>
    public static string StageAndPromoteArchive(
        string enginesRootDir,
        string engineId,
        string version,
        string verifiedArchivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(enginesRootDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(engineId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        string cleanVer = version.Trim().TrimStart('v', 'V');
        string engineRoot = Path.Combine(Path.GetFullPath(enginesRootDir), engineId.Trim());
        string stagingDir = Path.Combine(engineRoot, $".staging-{cleanVer}");
        string finalDir = Path.Combine(engineRoot, $"v{cleanVer}");
        Directory.CreateDirectory(engineRoot);

        if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, recursive: true);
        Directory.CreateDirectory(stagingDir);

        ExtractArchiveSafely(verifiedArchivePath, stagingDir);
        if (Directory.Exists(finalDir)) Directory.Delete(finalDir, recursive: true);
        Directory.Move(stagingDir, finalDir);

        string readyJson = JsonSerializer.Serialize(new { engineId = engineId.Trim(), version = cleanVer, completedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") }, JsonOptions);
        File.WriteAllText(Path.Combine(finalDir, ".ready"), readyJson, Encoding.UTF8);
        return finalDir;
    }

    private static async Task StreamAndVerifyAsync(
        HttpResponseMessage resp,
        string partPath,
        string metaPath,
        UpdatePartMeta seedMeta,
        bool append,
        long initialOffset,
        CancellationToken ct)
    {
        long total = resp.Content.Headers.ContentLength.HasValue ? resp.Content.Headers.ContentLength.Value + initialOffset : seedMeta.ExpectedSizeBytes;
        await using (Stream net = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var fs = new FileStream(partPath, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
        {
            byte[] buf = new byte[65536];
            long written = initialOffset;
            while (true)
            {
                int n = await net.ReadAsync(buf, ct).ConfigureAwait(false);
                if (n <= 0) break;
                await fs.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                written += n;
                WriteMeta(metaPath, seedMeta with { ExpectedSizeBytes = total, FileSizeBytes = written, UpdatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") });
            }
            await fs.FlushAsync(ct).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(seedMeta.Sha256Hex))
        {
            await using FileStream checkStream = File.OpenRead(partPath);
            byte[] hash = await SHA256.HashDataAsync(checkStream, ct).ConfigureAwait(false);
            string actual = Convert.ToHexString(hash).ToLowerInvariant();
            if (!string.Equals(actual, seedMeta.Sha256Hex.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"SHA-256 mismatch on staged part '{partPath}': expected {seedMeta.Sha256Hex}, got {actual}.");
            }
        }
    }

    private static void ExtractArchiveSafely(string zipPath, string destinationDir)
    {
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            string candidate = Path.GetFullPath(Path.Combine(destinationDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            string contained = AppEnvironment.AssertPathContained(candidate, destinationDir);
            Directory.CreateDirectory(Path.GetDirectoryName(contained)!);
            entry.ExtractToFile(contained, overwrite: true);
        }
    }

    private static void AssertSafeIpAddress(IPAddress ip, bool allowLoopback)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip))
        {
            if (!allowLoopback) throw new InvalidOperationException($"SSRF Guard: blocked loopback address '{ip}'.");
            return;
        }
        byte[] b = ip.GetAddressBytes();
        bool blocked = ip.AddressFamily == AddressFamily.InterNetwork
            ? (b[0] == 10 || b[0] == 0 || (b[0] == 169 && b[1] == 254) || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168))
            : (ip.Equals(IPAddress.IPv6None) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || (b[0] & 0xFE) == 0xFC || (b[0] == 0xFE && (b[1] & 0xC0) == 0x80));
        if (blocked) throw new InvalidOperationException($"SSRF Guard: blocked private/link-local address '{ip}'.");
    }

    private static HttpClient CreateClient(HttpMessageHandler? customHandler, TimeSpan timeout) =>
        customHandler is not null
            ? new HttpClient(customHandler, disposeHandler: false) { Timeout = timeout }
            : new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(10), AutomaticDecompression = DecompressionMethods.All }, disposeHandler: true) { Timeout = timeout };

    private static UpdatePartMeta? TryReadMeta(string path)
    {
        try { return JsonSerializer.Deserialize<UpdatePartMeta>(File.ReadAllText(path, Encoding.UTF8), JsonOptions); }
        catch (Exception) { return null; }
    }

    private static void WriteMeta(string metaPath, UpdatePartMeta meta) =>
        File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, JsonOptions), new UTF8Encoding(false));
}
