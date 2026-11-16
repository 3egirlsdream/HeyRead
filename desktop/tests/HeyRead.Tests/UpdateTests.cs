using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HeyRead.Core;
using HeyRead.Core.Updates;
using HeyRead.Desktop.Updates;
using Xunit;
namespace HeyRead.Tests;

public sealed class UpdateTests
{
    [Fact]
    public void VersionsCompareNumericallyAndPreserveArchitecture()
    {
        Assert.True(GitHubUpdateService.ParseVersion("1.10.0") > GitHubUpdateService.ParseVersion("1.9.9"));
        Assert.Equal("xiaoheihe-community-formac-0.3.0-osx-x64.zip", GitHubUpdateService.AssetName("0.3.0", "osx-x64"));
        foreach (var invalid in new[] { "v1.0.0", "1.0", "1.0.0-beta", "1.2.3/../../x" }) Assert.Throws<InvalidDataException>(() => GitHubUpdateService.ParseVersion(invalid));
    }
    [Fact]
    public void ManifestAuthenticatesVersionIdentityAndBothArchitectures()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); var original = Manifest(); var bytes = JsonSerializer.SerializeToUtf8Bytes(original); var signature = Sign(key, bytes);
        var verified = GitHubUpdateService.VerifyManifest(bytes, signature, key.ExportSubjectPublicKeyInfoPem(), "0.3.0"); Assert.Equal(2, verified.Assets.Length);
        var tampered = bytes.ToArray(); tampered[^2] ^= 1; Assert.Throws<CryptographicException>(() => GitHubUpdateService.VerifyManifest(tampered, signature, key.ExportSubjectPublicKeyInfoPem(), "0.3.0"));
        foreach (var wrong in new[] { original with { Product = "other" }, original with { BundleIdentifier = "other.bundle" }, original with { Version = "0.4.0" }, original with { Assets = [original.Assets[0], original.Assets[0]] } })
        {
            var data = JsonSerializer.SerializeToUtf8Bytes(wrong); Assert.Throws<InvalidDataException>(() => GitHubUpdateService.VerifyManifest(data, Sign(key, data), key.ExportSubjectPublicKeyInfoPem(), "0.3.0"));
        }
    }
    [Fact]
    public async Task GitHubCheckSelectsSignedArchitectureAndIgnoresSameVersion()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); var manifest = Manifest(); var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest); var signature = Sign(key, bytes); var calls = 0;
        using var service = new GitHubUpdateService(new RouteHandler(request =>
        {
            calls++;
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("update-manifest.json")) return Bytes(bytes);
            if (path.EndsWith("update-manifest.sig")) return Bytes(signature);
            return Bytes(Release());
        }), key.ExportSubjectPublicKeyInfoPem());
        var update = await service.CheckAsync("0.2.0", "osx-x64", TestContext.Current.CancellationToken); Assert.NotNull(update); Assert.Equal("osx-x64", update.Asset.Rid); Assert.Equal(3, calls);
        Assert.Null(await service.CheckAsync("0.3.0", "osx-x64", TestContext.Current.CancellationToken)); Assert.Equal(4, calls);
    }
    [Fact]
    public async Task DownloadRejectsTamperedOrTruncatedPayloadAndRemovesPartialFile()
    {
        var root = Temp(); Directory.CreateDirectory(root);
        try
        {
            var original = "verified update bytes"u8.ToArray(); var digest = Convert.ToHexStringLower(SHA256.HashData(original)); var name = GitHubUpdateService.AssetName("0.3.0", "osx-arm64");
            var asset = new UpdateAsset("osx-arm64", name, GitHubUpdateService.AssetUri("0.3.0", name).AbsoluteUri, original.Length, digest); var update = new AvailableUpdate("0.3.0", "", "", asset);
            using (var service = new GitHubUpdateService(new RouteHandler(_ => Bytes(Encoding.UTF8.GetBytes(new string('x', original.Length))))))
                await Assert.ThrowsAsync<CryptographicException>(() => service.DownloadAsync(update, root, ct: TestContext.Current.CancellationToken));
            Assert.Empty(Directory.GetFiles(root));
            using (var service = new GitHubUpdateService(new RouteHandler(_ => Bytes(original))))
            {
                var downloaded = await service.DownloadAsync(update, root, ct: TestContext.Current.CancellationToken); Assert.Equal(original, File.ReadAllBytes(downloaded));
            }
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task UpdatesNeverSendCookiesAndRejectForeignRedirects()
    {
        var calls = 0; using var service = new GitHubUpdateService(new RouteHandler(request =>
        {
            calls++; Assert.False(request.Headers.Contains("Cookie")); var response = new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location = new("https://untrusted.example/update"); return response;
        }));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckAsync("0.2.0", "osx-arm64", TestContext.Current.CancellationToken)); Assert.Equal(1, calls);
    }
    [Theory]
    [InlineData("../outside", false)] [InlineData("xiaoheihe-community-formac.app/../../outside", false)] [InlineData("evil.app/Contents/Info.plist", false)] [InlineData("xiaoheihe-community-formac.app/link", true)]
    public void ArchiveRejectsTraversalForeignAppsAndSymbolicLinks(string entry, bool link)
    {
        var file = Path.GetTempFileName();
        try
        {
            File.Delete(file); using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
            {
                zip.CreateEntry(ProductInfo.PackageName + ".app/Contents/Info.plist"); var item = zip.CreateEntry(entry); if (link) item.ExternalAttributes = (0xA000 | 0x1FF) << 16;
            }
            Assert.Throws<InvalidDataException>(() => UpdateArchive.Validate(file));
        }
        finally { File.Delete(file); }
    }
    [Fact]
    public void HandoffScriptQuotesPathsAndKeepsBackupUntilHealthyStartup()
    {
        var plan = new InstallPlan("id", "/tmp/user's app.app", "/tmp/new app.app", "/tmp/backup.app", "0.3.0", 123, "/tmp/user data", true);
        var script = MacUpdateInstaller.BuildScript(new("/tmp/context", "/tmp/context/receipt.json", plan));
        Assert.Contains("'\"'\"'", script); Assert.Contains("kill -0", script); Assert.Contains("status completed", script); Assert.Contains("rollback", script); Assert.Contains("--offline-demo", script); Assert.Contains("--data-root", script);
        Assert.True(script.IndexOf("status completed", StringComparison.Ordinal) < script.IndexOf("/bin/rm -rf \"$BACKUP\"", StringComparison.Ordinal));
        var path = Path.GetTempFileName(); File.WriteAllText(path, script);
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/bin/bash") { ArgumentList = { "-n", path }, RedirectStandardError = true, UseShellExecute = false })!; process.WaitForExit(); Assert.Equal(0, process.ExitCode);
        }
        finally { File.Delete(path); }
    }
    private static UpdateManifest Manifest()
    {
        var assets = new[] { "osx-arm64", "osx-x64" }.Select(rid => { var file = GitHubUpdateService.AssetName("0.3.0", rid); return new UpdateAsset(rid, file, GitHubUpdateService.AssetUri("0.3.0", file).AbsoluteUri, 20, new string('a', 64)); }).ToArray();
        return new(1, ProductInfo.PackageName, ProductInfo.BundleId, ProductInfo.Repository, "0.3.0", "mac-v0.3.0", assets);
    }
    private static byte[] Release() => JsonSerializer.SerializeToUtf8Bytes(new { tag_name = "mac-v0.3.0", draft = false, prerelease = false, body = "Release notes", assets = new[] { "update-manifest.json", "update-manifest.sig", GitHubUpdateService.AssetName("0.3.0", "osx-x64") }.Select(name => new { name, browser_download_url = GitHubUpdateService.AssetUri("0.3.0", name).AbsoluteUri }) });
    private static byte[] Sign(ECDsa key, byte[] bytes) => key.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
    private static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private static string Temp() => Path.Combine(Path.GetTempPath(), "xiaoheihe-update-tests-" + Guid.NewGuid());
}
