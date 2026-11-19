using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace HeyRead.Desktop.Design;

/// <summary>Artwork-only regions from the approved visual reference, used exclusively by the opt-in visual fixture.</summary>
public sealed class AReferenceAssets : IDisposable
{
    private readonly Dictionary<string, Bitmap> _sources = [];
    private readonly string? _root = Environment.GetEnvironmentVariable("HEYREAD_DESIGN_REFERENCE_ROOT");
    private readonly bool _reader = Environment.GetEnvironmentVariable("HEYREAD_VISUAL_SCENE") == "reader";
    public bool Available => _root != null;
    public Control Artwork(string key, double height, double? width = null)
    {
        var image = new Image { Source = Image(key), Stretch = Stretch.UniformToFill };
        var border = new Border { Child = image, Height = height, CornerRadius = new(8), ClipToBounds = true };
        if (width != null) border.Width = width.Value;
        return border;
    }
    public IImage? Image(string key)
    {
        if (_root == null) return null;
        if (Path.GetExtension(_root).Equals(".png", StringComparison.OrdinalIgnoreCase))
            return CompactArtwork(key, _root);
        var file = key == "game" || key == "hero" && _reader ? "11-article.png" : "01-discover.png";
        if (key.EndsWith("-feed", StringComparison.Ordinal) || key == "feed-game") file = "02-recommended.png";
        if (key == "account-large") file = "16-account.png";
        var rect = key switch
        {
            "hero" => _reader ? new PixelRect(705, 219, 853, 264) : new PixelRect(824, 238, 739, 317),
            "avatar" => new PixelRect(834, 173, 41, 38),
            "account-large" => new PixelRect(291, 369, 117, 117),
            "account" => new PixelRect(36, 861, 37, 35),
            "game" => new PixelRect(712, 765, 170, 72),
            "1-feed" => new PixelRect(294, 228, 378, 191),
            "2-feed" => new PixelRect(294, 518, 378, 172),
            "5-feed" => new PixelRect(294, 785, 378, 170),
            "feed-game" => new PixelRect(1207, 251, 106, 105),
            "1" => new PixelRect(284, 208, 117, 78),
            "2" => new PixelRect(284, 332, 119, 82),
            "3" => new PixelRect(284, 457, 119, 82),
            "4" => new PixelRect(284, 583, 119, 82),
            "5" => new PixelRect(284, 709, 119, 82),
            _ => new PixelRect(284, 835, 119, 82)
        };
        if (!_sources.TryGetValue(file, out var source))
        {
            var path = Path.Combine(_root, "light", file);
            if (!File.Exists(path)) return null;
            source = new Bitmap(path); _sources[file] = source;
        }
        return new CroppedBitmap { Source = source, SourceRect = rect };
    }
    private IImage? CompactArtwork(string key, string path)
    {
        var region = key switch
        {
            "hero" => new PixelRect(450, 237, 404, 221),
            "avatar" => new PixelRect(451, 173, 37, 37),
            "comment-1" => new PixelRect(1325, 323, 36, 36),
            "reply-1" => new PixelRect(1374, 439, 27, 29),
            "comment-2" => new PixelRect(1325, 566, 37, 37),
            "reply-2" => new PixelRect(1374, 685, 27, 27),
            "1" => new PixelRect(162, 181, 84, 60),
            "2" => new PixelRect(162, 270, 84, 58),
            "3" => new PixelRect(162, 357, 84, 58),
            "4" => new PixelRect(162, 445, 84, 58),
            "5" => new PixelRect(162, 532, 84, 58),
            "6" => new PixelRect(162, 618, 84, 56),
            "7" => new PixelRect(162, 698, 84, 56),
            "8" => new PixelRect(162, 780, 84, 55),
            _ => default
        };
        if (region.Width == 0 || !File.Exists(path)) return null;
        if (!_sources.TryGetValue(path, out var source)) _sources[path] = source = new Bitmap(path);
        return new CroppedBitmap { Source = source, SourceRect = region };
    }
    public void Dispose() { foreach (var source in _sources.Values) source.Dispose(); _sources.Clear(); }
}
