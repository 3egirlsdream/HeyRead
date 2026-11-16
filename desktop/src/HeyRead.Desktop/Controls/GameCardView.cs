using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using HeyRead.Core;
using static HeyRead.Desktop.Controls.Ui;
namespace HeyRead.Desktop.Controls;
public sealed class GameCardView : Border
{
    private readonly GameDetailsService _details;
    private readonly ImageService _images;
    private readonly int _appId;
    private readonly bool _noImages;
    private readonly TextBlock _name;
    private readonly TextBlock _metadata = Text("Steam", 13, muted: true);
    private readonly ContentControl _cover = new();
    private readonly Func<Game, Task> _open;
    private Game _game;
    private CancellationTokenSource? _lifetime;
    public GameCardView(GameDetailsService details, ImageService images, int appId, string? title, bool noImages, Func<Game, Task> open)
    {
        _details = details; _images = images; _appId = appId; _noImages = noImages; _open = open;
        _game = new(appId, string.IsNullOrWhiteSpace(title) ? $"Steam App {appId}" : title, null); _name = Text(_game.Name, 16, true);
        var button = AsyncButton("", () => _open(_game)); button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Padding = new(8, 6);
        button.Content = Columns(noImages ? "0,0,*,20" : "138,14,*,20", _cover, new Border(), Stack(8, _name, _metadata), Icons.Get("chevron_right", 18, "Muted")); Child = button;
    }
    protected override async void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e); _lifetime = new(); var ct = _lifetime.Token;
        try { var data = await _details.LoadAsync(_appId, ct); if (ct.IsCancellationRequested) return; _game = data.ToGame(); _name.Text = data.Name; _metadata.Text = string.Join(" / ", new[] { data.Genres, data.Developer }.Where(x => x.Length > 0)); if (!_noImages) _cover.Content = new RemoteImage(_images, data.Image, 58) { Width = 138 }; }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!ct.IsCancellationRequested) _metadata.Text = "Steam · " + _appId; }
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { _lifetime?.Cancel(); _lifetime?.Dispose(); _lifetime = null; _cover.Content = null; base.OnDetachedFromVisualTree(e); }
}
