using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HeyRead.Core;
using HeyRead.Desktop.Controls;
using HeyRead.Desktop.Platform;
using QRCoder;
using static HeyRead.Desktop.Controls.Ui;
namespace HeyRead.Desktop.Views;

public sealed partial class MainWindow
{
    private Window Dialog(string title, double width, double height) => new() { Title = T(title), Width = width, Height = height, MinWidth = Math.Min(340, width), MinHeight = Math.Min(220, height), WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background, FontFamily = FontFamily };
    private async Task ShowDialogContent(string title, Control content, double width = 480, double height = 360)
    {
        var window = Dialog(title, width, height); var close = Button(T("关闭"), window.Close); var layout = new Grid { RowDefinitions = new("*,Auto"), Margin = new(24) }; layout.Children.Add(new ScrollViewer { Content = content }); Grid.SetRow(close, 1); close.HorizontalAlignment = HorizontalAlignment.Right; close.Margin = new(0, 16, 0, 0); layout.Children.Add(close); window.Content = layout; await window.ShowDialog(this);
    }
    private Task Notice(string message) => ShowDialogContent(ProductInfo.DisplayName, Text(T(message)), 460, 240);
    private async Task<bool> Confirm(string title, string description)
    {
        var window = Dialog(title, 420, 220); var yes = Button(T("确认"), () => window.Close(true), "primary"); var no = Button(T("取消"), () => window.Close(false)); window.Content = Stack(24, Text(T(title), 18, true), Text(T(description)), Row(8, no, yes)); ((Control)window.Content).Margin = new(24); return await window.ShowDialog<bool>(this);
    }
    private async Task LoginDialog()
    {
        var window = Dialog(T("登录小黑盒"), 380, 480); using var lifetime = new CancellationTokenSource(); Bitmap? qrBitmap = null;
        var image = new Image { Width = 220, Height = 220, Stretch = Stretch.Uniform };
        var state = Text(T("正在获取二维码…"), 13, muted: true); var refresh = new Button { Content = T("重新获取") };
        var verify = Button(T("官方验证入口"), () => Native.Verify("https://www.xiaoheihe.cn/"), "flat");
        window.Content = Stack(18, Text(T("用小黑盒 App 扫码登录"), 20, true), image, state, Row(8, refresh, verify)); ((Control)window.Content).Margin = new(28);
        CancellationTokenSource? attempt = null;
        async Task Start()
        {
            attempt?.Cancel(); attempt?.Dispose(); attempt = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); var token = attempt.Token; state.Text = T("正在获取二维码…"); refresh.IsEnabled = false; image.Source = null; qrBitmap?.Dispose(); qrBitmap = null;
            try
            {
                var qr = await _vm.Api.QrAsync(token);
                using var generator = new QRCodeGenerator(); using var data = generator.CreateQrCode(qr.Url, QRCodeGenerator.ECCLevel.M); using var code = new PngByteQRCode(data); using var stream = new MemoryStream(code.GetGraphic(8)); qrBitmap = new Bitmap(stream); image.Source = qrBitmap;
                state.Text = T("等待扫描 · 请使用小黑盒 App"); refresh.IsEnabled = true;
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(2000, token); var result = await _vm.Api.PollAsync(qr, token);
                    switch (result.State)
                    {
                        case "ready": state.Text = T("已扫描，请在手机上确认"); break;
                        case "expired": state.Text = T("二维码已过期，请重新获取"); image.Opacity = 0.25; return;
                        case "cancelled": state.Text = T("登录已取消，请重新获取"); return;
                        case "loggedin" when result.Session != null: var story = _vm.Article?.Story; _vm.Login(result.Session); state.Text = T("正在加载…"); await _vm.LoadAsync(); if (story != null) await _vm.SelectAsync(story); window.Close(); Render(); return;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (!token.IsCancellationRequested) state.Text = T("获取失败：") + T(e.Message); }
            finally { if (!token.IsCancellationRequested) refresh.IsEnabled = true; }
        }
        refresh.Click += async (_, _) => { image.Opacity = 1; await Start(); };
        window.Opened += async (_, _) => await Start(); window.Closed += (_, _) => { lifetime.Cancel(); attempt?.Cancel(); image.Source = null; qrBitmap?.Dispose(); };
        await window.ShowDialog(this); attempt?.Dispose();
    }
    private async Task CommentDialog(Comment? reply)
    {
        if (_vm.Api.Session == null) { await LoginDialog(); if (_vm.Api.Session == null) return; }
        var snapshot = _vm.Article; if (snapshot == null) return;
        var expectedAccount = _vm.Api.Session?.UserId;
        var key = expectedAccount + ":" + snapshot.Story.Key + ":" + (reply?.Id ?? "root");
        var window = Dialog(reply == null ? T("写评论") : T("回复") + " " + reply.Author.Name, 540, 340);
        var editor = new TextBox { Text = _vm.Store.Drafts.GetValueOrDefault(key, ""), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 150, MaxLength = 10000, PlaceholderText = T("写下你的想法…") };
        var error = Text(T("草稿自动保存在本机"), 12, muted: true);
        var send = AsyncButton(T("发送"), async () =>
        {
            var draft = editor.Text ?? ""; _vm.Store.SetDraft(key, draft);
            if (_vm.Api.Session?.UserId != expectedAccount) { error.Text = T("账号已变化，请重新打开评论编辑器"); return; }
            try { await _vm.Api.SocialAsync(snapshot.Story, "comment", true, draft, snapshot.AuthCode, reply); _vm.Store.SetDraft(key, ""); editor.Text = ""; window.Close(); _vm.Toast = "发送成功"; if (_vm.Article?.Story.Key == snapshot.Story.Key) await _vm.RetryDetailAsync(); }
            catch (Exception e) { error.Text = T("发送失败，草稿已保留：") + T(e.Message); }
        }, "primary");
        var draftTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) }; draftTimer.Tick += (_, _) => { draftTimer.Stop(); _vm.Store.SetDraft(key, editor.Text ?? ""); };
        editor.TextChanged += (_, _) => { draftTimer.Stop(); draftTimer.Start(); };
        window.Closed += (_, _) => { draftTimer.Stop(); _vm.Store.SetDraft(key, editor.Text ?? ""); };
        window.Content = Stack(16, Text(reply == null ? T("写评论") : $"{T("回复")} {reply.Author.Name}", 18, true), editor, error, Row(8, Button(T("取消"), window.Close), send)); ((Control)window.Content).Margin = new(24); await window.ShowDialog(this);
    }
    private async Task ImageViewer(string[] urls, int start)
    {
        if (urls.Length == 0) return; var index = Math.Clamp(start, 0, urls.Length - 1); var window = Dialog("图片查看器", 1000, 740); window.MinWidth = 640; window.MinHeight = 440;
        using var lifetime = new CancellationTokenSource(); var host = new ContentControl(); var status = Text(""); var scroll = new ScrollViewer { Content = host, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Bitmap? bitmap = null; string? local = null; double zoom = 1; bool fitted = true; CancellationTokenSource? load = null;
        void Fit() { fitted = true; if (bitmap == null) return; host.Width = Math.Max(1, scroll.Bounds.Width - 16); host.Height = Math.Max(1, scroll.Bounds.Height - 16); if (host.Content is Image i) i.Stretch = Stretch.Uniform; zoom = Math.Min(host.Width / bitmap.PixelSize.Width, host.Height / bitmap.PixelSize.Height); }
        scroll.SizeChanged += (_, _) => { if (fitted) Fit(); };
        async Task Load()
        {
            load?.Cancel(); load?.Dispose(); load = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); var token = load.Token; status.Text = T("正在加载…"); host.Content = null; bitmap?.Dispose(); bitmap = null; local = null;
            try
            {
                var fetched = await _images.FetchAsync(urls[index], token); token.ThrowIfCancellationRequested(); var decoded = await Task.Run(() => ImageService.Decode(fetched, 2048), token); if (token.IsCancellationRequested) { decoded.Dispose(); return; } local = fetched; bitmap = decoded; var signature = new byte[6]; using (var source = File.OpenRead(local)) _ = source.Read(signature);
                host.Content = OperatingSystem.IsMacOS() && System.Text.Encoding.ASCII.GetString(signature).StartsWith("GIF8") ? new NativeGif(local) : new Image { Source = bitmap, Stretch = Stretch.Uniform }; status.Text = $"{index + 1} / {urls.Length}"; Fit();
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (!token.IsCancellationRequested) status.Text = e.Message; }
        }
        var save = AsyncButton(T("保存图片"), async () =>
        {
            var filePath = local; if (filePath == null) return;
            var file = await window.StorageProvider.SaveFilePickerAsync(new() { Title = T("保存图片"), SuggestedFileName = ProductInfo.PackageName + "-image" + ImageService.Extension(filePath) });
            if (file != null) { await using var output = await file.OpenWriteAsync(); if (output.CanSeek) output.SetLength(0); await using var input = File.OpenRead(filePath); await input.CopyToAsync(output); }
        });
        var tools = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8 };
        foreach (var tool in new Control[] { AsyncButton(T("上一张"), async () => { index = (index - 1 + urls.Length) % urls.Length; await Load(); }), AsyncButton(T("下一张"), async () => { index = (index + 1) % urls.Length; await Load(); }), Button(T("适应窗口"), Fit), save, AsyncButton(T("分享"), async () =>
        {
            var filePath = local; if (filePath == null) { Native.Share(urls[index]); return; }
            var directory = Path.Combine(_vm.Store.Root, "cache", "share"); Directory.CreateDirectory(directory);
            var destination = Path.Combine(directory, Path.GetFileName(filePath) + ImageService.Extension(filePath));
            await using (var input = File.OpenRead(filePath)) await using (var output = File.Create(destination)) await input.CopyToAsync(output);
            Native.Share(new Uri(destination).AbsoluteUri);
        }), status }) tools.Children.Add(tool);
        var grid = new Grid { RowDefinitions = new("Auto,*"), Margin = new(16), RowSpacing = 12 }; grid.Children.Add(tools); Grid.SetRow(scroll, 1); grid.Children.Add(scroll); window.Content = grid;
        scroll.PointerWheelChanged += (_, e) => { if (bitmap == null || !e.KeyModifiers.HasFlag(KeyModifiers.Meta)) return; fitted = false; zoom = Math.Clamp(zoom * (e.Delta.Y > 0 ? 1.2 : 1 / 1.2), 0.1, 8); host.Width = bitmap.PixelSize.Width * zoom; host.Height = bitmap.PixelSize.Height * zoom; if (host.Content is Image i) i.Stretch = Stretch.Fill; e.Handled = true; };
        window.KeyDown += async (_, e) => { if (e.Key == Key.Escape) window.Close(); if (e.Key is Key.Left or Key.Right) { index = (index + (e.Key == Key.Right ? 1 : urls.Length - 1)) % urls.Length; await Load(); } };
        window.Opened += async (_, _) => await Load(); window.Closed += (_, _) => { lifetime.Cancel(); load?.Cancel(); host.Content = null; bitmap?.Dispose(); }; await window.ShowDialog(this); load?.Dispose();
    }
    private async Task RepliesDialog(Comment root)
    {
        var window = Dialog("楼中楼", 580, 560); var list = Stack(16, CommentView(root, true)); string? cursor = null; var seen = new HashSet<string> { root.Id };
        var more = new Button { Content = T("加载更多") }; var status = Text("", 12, muted: true);
        async Task Load()
        {
            more.IsEnabled = false;
            try { var page = await _vm.RepliesAsync(root, cursor); foreach (var c in page.Items) if (seen.Add(c.Id)) list.Children.Add(CommentView(c, true)); more.IsVisible = page.HasMore && page.Cursor != cursor; cursor = page.Cursor; status.Text = ""; }
            catch (Exception e) { status.Text = e.Message; }
            finally { more.IsEnabled = true; }
        }
        more.Click += async (_, _) => await Load(); var layout = new Grid { RowDefinitions = new("*,Auto"), Margin = new(24) }; layout.Children.Add(new ScrollViewer { Content = list }); var footer = Stack(8, status, more); Grid.SetRow(footer, 1); layout.Children.Add(footer); window.Content = layout; window.Opened += async (_, _) => await Load(); await window.ShowDialog(this);
    }
    private async Task RuleDialog(BlockRule? existing = null)
    {
        var window = Dialog(T(existing == null ? "添加规则" : "编辑"), 480, 380);
        var kind = new ComboBox { ItemsSource = Enum.GetNames<RuleKind>().Select(k => new ChoiceOption(k, T(k))).ToArray(), SelectedIndex = (int)(existing?.Kind ?? RuleKind.KEYWORD), HorizontalAlignment = HorizontalAlignment.Stretch };
        var value = new TextBox { Text = existing?.Value ?? "", PlaceholderText = T("关键词或 ID") }; var label = new TextBox { Text = existing?.Label ?? "", PlaceholderText = T("备注（身份规则可选）") }; var error = Text(T("关键词匹配标题与摘要；身份规则使用小黑盒 ID。"), 12, muted: true);
        window.Content = Stack(16, Text(T("内容屏蔽"), 20, true), kind, value, label, error, Row(8, Button(T("取消"), window.Close), Button(T("保存"), () => { try { var k = Enum.Parse<RuleKind>(((ChoiceOption)kind.SelectedItem!).Value); var rule = new BlockRule(k, value.Text ?? "", k == RuleKind.KEYWORD ? "" : label.Text ?? "", existing?.Enabled ?? true, k == RuleKind.KEYWORD ? null : Source.HEYBOX).Validate(); if (existing != null)
                    {
                        var index = _vm.Prefs.Rules.FindIndex(r => r.Identity == existing.Identity);
                        if (index < 0) throw new ArgumentException("规则已变化，请重新编辑");
                        if (_vm.Prefs.Rules.Where((_, i) => i != index).Any(r => r.Identity == rule.Identity)) throw new ArgumentException("相同规则已存在");
                        _vm.Prefs.Rules[index] = rule; _vm.SaveSettings(); _vm.ApplyFilter();
                    }
                    else AddRule(rule); window.Close(); } catch (Exception e) { error.Text = e.Message; } }, "primary"))); ((Control)window.Content).Margin = new(24); await window.ShowDialog(this);
    }
    private void AddRule(BlockRule rule)
    {
        var validated = rule.Validate(); if (_vm.Prefs.Rules.Count >= 500) throw new ArgumentException("规则最多 500 条"); if (!_vm.Prefs.Rules.Any(x => x.Identity == validated.Identity)) _vm.Prefs.Rules.Add(validated); _vm.SaveSettings(); _vm.ApplyFilter();
    }
    private async Task ImportRules()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = T("导入规则"), AllowMultiple = false, FileTypeFilter = [new("JSON") { Patterns = ["*.json"] }] }); if (files.Count == 0) return;
        await using var input = await files[0].OpenReadAsync(); using var output = new MemoryStream(); var buffer = new byte[8192]; int n; while ((n = await input.ReadAsync(buffer)) > 0) { if (output.Length + n > 262144) throw new ArgumentException("规则文件超过 256 KiB"); output.Write(buffer, 0, n); }
        var import = BlockingCodec.Import(output.ToArray()); var added = import.Rules.Where(x => !_vm.Prefs.Rules.Any(r => r.Identity == x.Identity)).ToArray();
        if (_vm.Prefs.Rules.Count + added.Length > 500) throw new ArgumentException("合并后超过 500 条规则");
        var details = string.Join("\n", Enum.GetValues<RuleKind>().Select(k => $"{T(k.ToString())}: {added.Count(r => r.Kind == k)}"));
        if (await Confirm("导入预览", F("新增 {0} 条，重复 {1} 条。\n{2}", added.Length, import.Duplicates + import.Rules.Count - added.Length, details))) { _vm.Store.Merge(import); _vm.SaveSettings(); Render(); }
    }
    private async Task ExportRules()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new() { Title = T("导出规则"), SuggestedFileName = "heyread-content-blocking.json", FileTypeChoices = [new("JSON") { Patterns = ["*.json"] }] }); if (file == null) return; var bytes = BlockingCodec.Export(_vm.Prefs.Rules); await using var stream = await file.OpenWriteAsync(); await stream.WriteAsync(bytes);
    }
}
