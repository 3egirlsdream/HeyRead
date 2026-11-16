using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HeyRead.Core;
using HeyRead.Desktop.Platform;
namespace HeyRead.Desktop.Controls;

public sealed class ImageService(string cacheRoot, HttpMessageHandler? handler = null) : IDisposable
{
    private readonly HttpClient _http = new(handler ?? new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    private readonly SemaphoreSlim _slots = new(4);
    public async Task<string> FetchAsync(string url, CancellationToken ct)
    {
        if (J.Https(url) == null) throw new IOException("图片地址不是 HTTPS");
        var path = Path.Combine(cacheRoot, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url))));
        if (File.Exists(path)) { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); return path; }
        await _slots.WaitAsync(ct);
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct); response.EnsureSuccessStatusCode(); var bytes = await HeyReadApi.ReadLimitedAsync(response, 20 * 1024 * 1024, ct);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp"; try { await File.WriteAllBytesAsync(temp, bytes, ct); File.Move(temp, path, true); } finally { if (File.Exists(temp)) File.Delete(temp); }
            var files = new DirectoryInfo(cacheRoot).GetFiles().OrderByDescending(f => f.LastWriteTimeUtc).ToArray(); long size = 0; foreach (var file in files) { size += file.Length; if (size > 128 * 1024 * 1024 && file.FullName != path) try { file.Delete(); } catch (IOException) { } }
            return path;
        }
        finally { _slots.Release(); }
    }
    public static Bitmap Decode(string path, int maxSide)
    {
        int width, height;
        using (var input = File.OpenRead(path))
        using (var codec = SkiaSharp.SKCodec.Create(input))
        {
            if (codec == null) throw new IOException("图片格式不受支持");
            width = codec.Info.Width; height = codec.Info.Height;
        }
        if (width <= 0 || height <= 0 || width > 100000 || height > 100000) throw new IOException("图片尺寸无效");
        using var stream = File.OpenRead(path);
        return height > width ? Bitmap.DecodeToHeight(stream, Math.Min(height, maxSide)) : Bitmap.DecodeToWidth(stream, Math.Min(width, maxSide));
    }
    public void Dispose() { _http.Dispose(); _slots.Dispose(); }
}
public sealed class RemoteImage : Border
{
    private readonly ImageService _service; private readonly string? _url; private readonly Image _image = new() { Stretch = Stretch.UniformToFill }; private CancellationTokenSource? _cts; private Bitmap? _bitmap;
    public bool IsImageLoaded => _bitmap != null || Child is NativeGif;
    public RemoteImage(ImageService service, string? url, double height = 64)
    {
        _service = service; _url = url; Height = height; CornerRadius = new(8); ClipToBounds = true; Ui.Brush(this, BackgroundProperty, "Subtle"); Child = _image;
    }
    protected override async void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e); Child = _image; if (_url == null) return; _cts = new(); var token = _cts.Token;
        try
        {
            var path = await _service.FetchAsync(_url, token); if (OperatingSystem.IsMacOS()) { var header = new byte[6]; using (var file = File.OpenRead(path)) _ = file.Read(header); if (Encoding.ASCII.GetString(header).StartsWith("GIF8")) { if (!token.IsCancellationRequested) Child = new NativeGif(path); return; } }
            var decodeWidth = Height <= 100 ? 240 : 1600;
            var bitmap = await Task.Run(() => { return ImageService.Decode(path, decodeWidth); }, token); if (token.IsCancellationRequested) bitmap.Dispose(); else { _bitmap = bitmap; _image.Source = bitmap; }
        }
        catch (Exception error) when (error is not OutOfMemoryException) { if (!token.IsCancellationRequested) Child = Ui.Text("图片暂不可用", 12, muted: true); }
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { _cts?.Cancel(); _cts?.Dispose(); _cts = null; _image.Source = null; _bitmap?.Dispose(); _bitmap = null; base.OnDetachedFromVisualTree(e); }
}
