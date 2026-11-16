using System.Net;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace HeyRead.Core.Updates;

public sealed record UpdateAsset(string Rid, string File, string Url, long Size, string Sha256);
public sealed record UpdateManifest(int Schema, string Product, string BundleIdentifier, string Repository, string Version, string Tag, UpdateAsset[] Assets);
public sealed record AvailableUpdate(string Version, string Notes, string ReleaseUrl, UpdateAsset Asset);
public sealed record UpdateProgress(double Percentage, string State);

public sealed class GitHubUpdateService : IDisposable
{
    public static string CurrentRid => RuntimeInformation.ProcessArchitecture switch { Architecture.Arm64 => "osx-arm64", Architecture.X64 => "osx-x64", _ => throw new PlatformNotSupportedException("不支持当前处理器的自动更新") };
    private readonly HttpClient _http;
    private readonly string _publicKey;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    public GitHubUpdateService(HttpMessageHandler? handler = null, string? verificationKey = null)
    {
        _http = new(handler ?? new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(ProductInfo.PackageName + "/" + ProductInfo.Version);
        using var stream = typeof(GitHubUpdateService).Assembly.GetManifestResourceStream("HeyRead.Core.UpdatePublicKey.pem")!;
        using var reader = new StreamReader(stream); _publicKey = verificationKey ?? reader.ReadToEnd();
    }
    public static Version ParseVersion(string value)
    {
        if (value.Length > 32 || !Regex.IsMatch(value, @"^[0-9]+\.[0-9]+\.[0-9]+$", RegexOptions.CultureInvariant) || !Version.TryParse(value, out var version)) throw new InvalidDataException("发布版本号格式无效");
        return version;
    }
    public static string AssetName(string version, string rid, string extension = "zip")
    {
        _ = ParseVersion(version); if (rid is not ("osx-arm64" or "osx-x64") || extension is not ("zip" or "dmg")) throw new InvalidDataException("更新包类型无效");
        return $"{ProductInfo.PackageName}-{version}-{rid}.{extension}";
    }
    public static Uri AssetUri(string version, string file) => new($"https://github.com/{ProductInfo.Repository}/releases/download/{ProductInfo.TagPrefix}{version}/{Uri.EscapeDataString(file)}");
    public async Task<AvailableUpdate?> CheckAsync(string currentVersion, string? rid = null, CancellationToken ct = default)
    {
        var current = ParseVersion(currentVersion); rid ??= CurrentRid;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(45)); var token = timeout.Token;
        using var response = await SendAsync(new($"https://api.github.com/repos/{ProductInfo.Repository}/releases/latest"), token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode(); var root = JsonNode.Parse(await HeyReadApi.ReadLimitedAsync(response, 2 * 1024 * 1024, token));
        if (!IsMacRelease(root))
        {
            using var listing = await SendAsync(new($"https://api.github.com/repos/{ProductInfo.Repository}/releases?per_page=30"), token); listing.EnsureSuccessStatusCode();
            root = JsonNode.Parse(await HeyReadApi.ReadLimitedAsync(listing, 2 * 1024 * 1024, token))?.AsArray().Where(IsMacRelease).OrderByDescending(x => ParseVersion(x!["tag_name"]!.GetValue<string>()[ProductInfo.TagPrefix.Length..])).FirstOrDefault();
        }
        if (!IsMacRelease(root)) return null;
        var version = root!["tag_name"]!.GetValue<string>()[ProductInfo.TagPrefix.Length..]; if (ParseVersion(version) <= current) return null;
        var assets = root["assets"] as JsonArray ?? throw new InvalidDataException("发布缺少更新资源");
        Uri Find(string file)
        {
            var asset = assets.SingleOrDefault(a => a?["name"]?.GetValue<string>() == file) ?? throw new InvalidDataException("发布缺少更新资源：" + file);
            var uri = new Uri(asset["browser_download_url"]!.GetValue<string>());
            if (uri != AssetUri(version, file)) throw new InvalidDataException("更新资源地址与发布版本不符"); return uri;
        }
        var bytes = await GetBytesAsync(Find("update-manifest.json"), 65536, token);
        var signature = await GetBytesAsync(Find("update-manifest.sig"), 256, token);
        var manifest = VerifyManifest(bytes, signature, _publicKey, version);
        var selected = manifest.Assets.SingleOrDefault(a => a.Rid == rid) ?? throw new InvalidDataException("发布不支持当前处理器");
        if (Find(selected.File).AbsoluteUri != selected.Url) throw new InvalidDataException("更新资源地址不匹配");
        return new(version, root["body"]?.GetValue<string>() is { } notes ? notes[..Math.Min(notes.Length, 16000)] : "", $"https://github.com/{ProductInfo.Repository}/releases/tag/{ProductInfo.TagPrefix}{version}", selected);
    }
    private static bool IsMacRelease(JsonNode? node)
    {
        if (node is not JsonObject || node["draft"]?.GetValue<bool>() == true || node["prerelease"]?.GetValue<bool>() == true) return false;
        var tag = node["tag_name"]?.GetValue<string>() ?? "";
        if (!tag.StartsWith(ProductInfo.TagPrefix, StringComparison.Ordinal)) return false;
        try { _ = ParseVersion(tag[ProductInfo.TagPrefix.Length..]); return true; } catch (InvalidDataException) { return false; }
    }
    public static UpdateManifest VerifyManifest(byte[] bytes, byte[] signature, string publicKey, string expectedVersion)
    {
        using var key = ECDsa.Create(); key.ImportFromPem(publicKey);
        if (!key.VerifyData(bytes, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)) throw new CryptographicException("更新清单签名校验失败");
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(bytes, JsonOptions) ?? throw new InvalidDataException("更新清单为空");
        if (manifest.Schema != 1 || manifest.Product != ProductInfo.PackageName || manifest.BundleIdentifier != ProductInfo.BundleId || manifest.Repository != ProductInfo.Repository || manifest.Version != expectedVersion || manifest.Tag != ProductInfo.TagPrefix + expectedVersion) throw new InvalidDataException("更新清单与当前应用不匹配");
        _ = ParseVersion(manifest.Version);
        if (manifest.Assets.Length != 2 || manifest.Assets.Select(a => a.Rid).Distinct().Count() != 2) throw new InvalidDataException("更新清单处理器列表无效");
        foreach (var asset in manifest.Assets)
            if (asset.File != AssetName(manifest.Version, asset.Rid) || asset.Url != AssetUri(manifest.Version, asset.File).AbsoluteUri || asset.Size is < 1 or > 1073741824 || !Regex.IsMatch(asset.Sha256, "^[a-fA-F0-9]{64}$")) throw new InvalidDataException("更新清单资源无效");
        return manifest;
    }
    public async Task<string> DownloadAsync(AvailableUpdate update, string directory, IProgress<UpdateProgress>? progress = null, CancellationToken ct = default)
    {
        var asset = update.Asset; if (asset.File != AssetName(update.Version, asset.Rid) || asset.Url != AssetUri(update.Version, asset.File).AbsoluteUri || asset.Size is < 1 or > 1073741824) throw new InvalidDataException("更新资源无效");
        Directory.CreateDirectory(directory); var final = Path.Combine(directory, asset.File); var partial = final + ".part";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(20)); var token = timeout.Token;
        try
        {
            using var response = await SendAsync(new(asset.Url), token); response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != asset.Size) throw new InvalidDataException("更新包大小与签名清单不符");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long total = 0; var buffer = new byte[81920]; var clock = Stopwatch.StartNew();
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Options = FileOptions.Asynchronous }; if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(partial, options))
            {
                int count; while ((count = await input.ReadAsync(buffer, token)) > 0)
                {
                    total += count; if (total > asset.Size) throw new InvalidDataException("更新包超过签名清单大小");
                    hash.AppendData(buffer, 0, count); await output.WriteAsync(buffer.AsMemory(0, count), token); if (clock.ElapsedMilliseconds >= 100 || total == asset.Size) { progress?.Report(new(total * 100d / asset.Size, "正在下载更新…")); clock.Restart(); }
                }
            }
            if (total != asset.Size || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(asset.Sha256))) throw new CryptographicException("更新包 SHA-256 校验失败");
            File.Move(partial, final); return final;
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }
    private async Task<byte[]> GetBytesAsync(Uri uri, int limit, CancellationToken ct)
    {
        using var response = await SendAsync(uri, ct); response.EnsureSuccessStatusCode(); return await HeyReadApi.ReadLimitedAsync(response, limit, ct);
    }
    private async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken ct)
    {
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            if (uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) || uri.Port != 443 || uri.Host is not ("github.com" or "api.github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com" or "github-releases.githubusercontent.com")) throw new InvalidDataException("已阻止不受信任的更新地址");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and <= 399 && response.Headers.Location != null) { var next = new Uri(uri, response.Headers.Location); response.Dispose(); uri = next; continue; }
            return response;
        }
        throw new HttpRequestException("更新下载重定向次数过多");
    }
    public void Dispose() => _http.Dispose();
}
