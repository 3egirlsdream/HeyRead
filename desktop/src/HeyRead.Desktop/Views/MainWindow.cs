using Avalonia.Input.Platform;
using HeyRead.Desktop.Design;
using System.ComponentModel;
using Avalonia.VisualTree;
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Documents;
using System.Text.Json;
using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using HeyRead.Core;
using HeyRead.Desktop.Controls;
using HeyRead.Desktop.Platform;
using HeyRead.Desktop.Resources;
using HeyRead.Desktop.ViewModels;
using static HeyRead.Desktop.Controls.Ui;
namespace HeyRead.Desktop.Views;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ImageService _images;
    private readonly GameDetailsService _gameDetails;
    private Strings _strings;
    private string T(string key) => _strings[key];
    private string F(string key, params object?[] args) => string.Format(System.Globalization.CultureInfo.CurrentCulture, T(key), args);
    private readonly AReferenceAssets _referenceAssets = new();
    private readonly Grid _root = new();
    private readonly ContentControl _body = new();
    private readonly ContentControl _header = new();
    private readonly ContentControl _sidebar = new();
    private readonly ContentControl _detail = new();
    private readonly TextBlock _title = Text("", 17, true);
    private readonly TextBlock _status = Text("", 12, muted: true);
    private readonly TextBox _search = new() { MaxLength = 120, MinWidth = 190,  HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ListBox _list = new();
    private readonly Grid _readingGrid = new();
    private readonly ContentControl _listHeader = new();
    private readonly ContentControl _listFooter = new();
    private readonly ScrollViewer _readerScroll = new();
    private bool _narrow, _showDetail, _rendering;
    private string? _renderedKey;
    private double _articleOffset, _commentOffset;
    public MainWindow(MainViewModel vm)
    {
        _vm = vm; DataContext = vm; _strings = new(vm.Prefs.Language); _images = new(Path.Combine(vm.Store.Root, "cache")); _gameDetails = new(vm.Api);
        Title = _vm.VisualFixture ? ProductInfo.DisplayName + " — A 视觉样例" : ProductInfo.DisplayName; Width = 1280; Height = 800; MinWidth = 900; MinHeight = 640;
        if (_vm.VisualFixture && Environment.GetEnvironmentVariable("HEYREAD_VISUAL_VIEWPORT") == "narrow") { Width = 900; Height = 640; }
        WindowStartupLocation = WindowStartupLocation.CenterScreen; ExtendClientAreaToDecorationsHint = true; ExtendClientAreaTitleBarHeightHint = 48;
        _root.ColumnDefinitions = new("210,*"); Content = _root; _root.Children.Add(_sidebar);
        var main = new Grid { RowDefinitions = new("68,*,0") }; Grid.SetColumn(main, 1); _root.Children.Add(main);
        _header.Content = BuildHeader(); main.Children.Add(_header); Grid.SetRow(_body, 1); main.Children.Add(_body);
        var statusBorder = Brush(new Border { Child = _status, Padding = new Thickness(20, 4), BorderThickness = new Thickness(0, 1, 0, 0) }, Border.BorderBrushProperty, "Line"); Grid.SetRow(statusBorder, 2); main.Children.Add(statusBorder);
        _list.ItemsSource = vm.Stories; _list.ItemTemplate = new FuncDataTemplate<Story>((s, _) => StoryRow(s!));
        _list.SelectionChanged += async (_, _) =>
        {
            if (_rendering || _list.SelectedItem is not Story story) return;
            SaveOffsets();
            _showDetail = true;
            await vm.SelectAsync(story);
            if (_overviewRows != DirectoryOverview) Render();
            else UpdateResponsive();
        };
        _readerScroll.ScrollChanged += (_, _) => { if (_rendering || vm.Article == null) return; if (vm.CommentsMode) _commentOffset = _readerScroll.Offset.Y; else _articleOffset = _readerScroll.Offset.Y; ScheduleViewportSave(); };
        _search.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { SaveOffsets(); vm.Query = _search.Text ?? ""; _showDetail = false; await vm.SearchAsync(); e.Handled = true; } };
        _search.TextChanged += (_, _) => vm.Query = _search.Text ?? "";
        vm.ContentChanged += Render;
        vm.SettingsChanged += OnSettingsChanged;
        vm.PropertyChanged += OnViewModelChanged;
        SizeChanged += (_, e) =>
        {
            UpdateResponsive();
            if (DirectoryOverview && (e.PreviousSize.Width >= 1180) != (e.NewSize.Width >= 1180))
                _list.ItemTemplate = new FuncDataTemplate<Story>((story, _) => StoryRow(story));
            if (!_rendering && _vm.Page is not ("settings" or "account" or "games")) RenderReader();
        }; KeyDown += OnKeys;
        if (Application.Current?.PlatformSettings is { } settings) settings.ColorValuesChanged += OnSystemColorsChanged;
        Opened += async (_, _) => { _notifications = new WindowNotificationManager(this) { MaxItems = 3, Position = NotificationPosition.BottomRight }; ApplyTheme(); _ = CheckAfterStartup(); await vm.InitializeAsync(); };
        Closing += (_, _) => { SaveOffsets(); vm.Store.SavePreferences(); };
        Closed += (_, _) => { _updateLifetime.Cancel(); _updates.Dispose(); _viewportTimer?.Stop(); _navigationMotion?.Cancel(); _navigationMotion?.Dispose(); if (Application.Current?.PlatformSettings is { } settings) settings.ColorValuesChanged -= OnSystemColorsChanged; if (_listScroll != null) _listScroll.ScrollChanged -= OnListScrollChanged; vm.SettingsChanged -= OnSettingsChanged; vm.PropertyChanged -= OnViewModelChanged; vm.ContentChanged -= Render; _readerScroll.Content = null; vm.Suspend(); _images.Dispose(); _referenceAssets.Dispose(); };
        ConfigureMenus(); ApplyTheme(); RenderSidebar(); Render();
    }
    private async Task Navigate(string page)
    {
        SaveOffsets(); _showDetail = false; _renderedKey = null; _vm.SearchScope = null; await _vm.NavigateAsync(page); _search.Text = _vm.Query;
    }
    private void Render()
    {
        if (_rendering) return; _rendering = true;
        try
        {
            var key = _vm.CurrentViewport.CacheKey;
            if (_visiblePageKey != key) { _visiblePageKey = key; _renderedKey = null; _readerContentIdentity = null; _showDetail = _vm.CurrentViewport.ShowDetail; _pendingListOffset = _vm.CurrentViewport.ListOffset; AnimateNavigation(); }
            _title.Text = T(_vm.Title); if (_search.Text != _vm.Query) _search.Text = _vm.Query; RenderSidebar();
            if (_vm.Page is "settings" or "account" or "games") { _readerScroll.Content = null; _readerContentIdentity = null; }
            if (_vm.Page == "settings") { _body.Content = BuildSettings(); return; }
            if (_vm.Page == "account") { _body.Content = BuildAccount(); return; }
            if (_vm.Page == "search" && string.IsNullOrWhiteSpace(_vm.CurrentViewport.Query)) { _body.Content = BuildSearchLanding(); return; }
            if (_vm.Page == "games") { _body.Content = BuildGames(); return; }
            Detach(_listHeader); Detach(_list); Detach(_listFooter);
            _readingGrid.Children.Clear(); _readingGrid.ColumnDefinitions = new($"{Math.Clamp(_vm.Prefs.ListWidth, 280, 480)},1,*");
            var listPanel = new Grid { RowDefinitions = new("Auto,*,Auto") }; listPanel.Children.Add(_listHeader); Grid.SetRow(_list, 1); listPanel.Children.Add(_list); Grid.SetRow(_listFooter, 2); listPanel.Children.Add(_listFooter);
            if (_vm.Loading && _vm.Stories.Count == 0) { var skeleton = BuildListSkeleton(); Grid.SetRow(skeleton, 1); listPanel.Children.Add(skeleton); }
            var listBorder = Brush(new Border { Child = listPanel, BorderThickness = new Thickness(0, 0, 1, 0) }, Border.BorderBrushProperty, "Line"); _readingGrid.Children.Add(listBorder);
            var splitter = new GridSplitter { Width = 1, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent }; Grid.SetColumn(splitter, 1); _readingGrid.Children.Add(splitter);
            splitter.DragCompleted += (_, _) => { _vm.Prefs.ListWidth = Math.Clamp(_readingGrid.ColumnDefinitions[0].ActualWidth, 280, 480); _vm.SaveSettings(); };
            Grid.SetColumn(_detail, 2); _readingGrid.Children.Add(_detail); _body.Content = _readingGrid;
            if (_overviewRows != DirectoryOverview)
            {
                _overviewRows = DirectoryOverview;
                _list.ItemTemplate = new FuncDataTemplate<Story>((story, _) => StoryRow(story));
            }
            _listHeader.Content = BuildListHeader();
            _listFooter.Content = BuildListFooter();
            if (_vm.VisualFixture && !DirectoryOverview && (_showDetail || Bounds.Width >= 1120) && _vm.Article != null && _list.SelectedItem == null)
                _list.SelectedItem = _vm.Stories.FirstOrDefault(x => x.Key == _vm.Article.Story.Key);
            RenderReader();
            UpdateResponsive();
            _status.Text = _vm.Loading ? T("正在加载…") : _vm.Demo ? "离线演示内容 · 不代表在线资讯" : $"{_vm.Stories.Count} 篇内容" + (_vm.BlockedCount > 0 ? $" · 已屏蔽 {_vm.BlockedCount} 篇" : "");
        }
        finally
        {
            _rendering = false;
            Dispatcher.UIThread.Post(() => { TrackListScroll(); if (!_vm.Loading && _pendingListOffset is { } offset && _listScroll != null) { _listScroll.Offset = new(0, offset); _pendingListOffset = null; } }, DispatcherPriority.Loaded);
        }
    }
    private Control BuildListHeader()
    {
        var children = Stack(10); children.Margin = new(14, 18, 12, 10);
        if (_vm.Page == "discover")
        {
            var tabs = Row(6);
            foreach (var (value, name) in new[] { ("all", "推荐"), ("news", "资讯") }) { var b = AsyncButton(T(name), async () => { _vm.Channel = value; await _vm.LoadAsync(); }, value == _vm.Channel ? "primary" : null); b.Height = 35; b.MinWidth = 70; b.FontSize = 16; tabs.Children.Add(b); }
            children.Children.Add(tabs); var line = Line(); line.Margin = new(0, 6, 0, 0); children.Children.Add(line);
        }
        if (_vm.Page is "dynamic" or "following")
        {
            var tabs = Row(6);
            foreach (var (page, label, description) in new[] { ("dynamic", "推荐", "推荐动态"), ("following", "关注", "关注动态") })
            {
                var tab = AsyncButton(T(label), () => Navigate(page), page == _vm.Page ? "primary" : null);
                tab.Classes.Add("sectionTab");
                ToolTip.SetTip(tab, T(description));
                tabs.Children.Add(tab);
            }
            children.Margin = new(18, 9, 18, 0);
            children.Spacing = 14;
            children.Children.Add(tabs);
            children.Children.Add(Line());
        }
        if (_vm.Page is "history" or "cloudhistory") children.Children.Add(Row(6, AsyncButton(T("本机历史"), () => Navigate("history"), _vm.Page == "history" ? "primary" : null), AsyncButton(T("账号历史"), () => Navigate("cloudhistory"), _vm.Page == "cloudhistory" ? "primary" : null)));
        if (_vm.Page == "following")
        {
            var authors = Row(6);
            foreach (var author in _vm.Following)
            {
                var filter = Button("", () => { _vm.FollowingFilter = author; _vm.ApplyFilter(); },
                    (_vm.FollowingFilter?.Id ?? "") == author.Id ? "selected" : "flat");
                filter.Content = Row(6, Avatar(_vm.Prefs.NoImages ? null : author.Avatar, 26), Text(T(author.Name), 13));
                authors.Children.Add(filter);
            }
            children.Children.Add(new ScrollViewer { Content = authors, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        }
        if (_vm.Page == "favorites") { var combo = new ComboBox { ItemsSource = _vm.Folders, ItemTemplate = new FuncDataTemplate<Folder>((f, _) => Text(T(f?.Name ?? ""))), SelectedItem = _vm.Folder ?? _vm.Folders.FirstOrDefault(), HorizontalAlignment = HorizontalAlignment.Stretch }; combo.SelectionChanged += async (_, _) => { if (!_vm.Loading && combo.SelectedItem is Folder f && f != _vm.Folder) { _vm.Folder = f; await _vm.LoadAsync(); } }; children.Children.Add(combo); }
        if (_vm.Page == "search")
        {
            children.Children.Add(Text(_vm.SearchScope == null ? $"“{_vm.Query}”" : $"{_vm.SearchScope.Name} · {_vm.Query}", 14, true));
            if (_vm.SearchScope?.Kind == "topic") children.Children.Add(Text(T("在话题列表中分批匹配标题与摘要"), 11, muted: true));
        }
        if (_vm.Page == "destination" && _vm.Target is { } target)
        {
            if (target.Kind is "author" or "topic" && !_vm.Prefs.NoImages) children.Children.Add(Cover(target.Kind == "topic" ? 105 : 134));
            children.Children.Add(Text(_vm.AuthorPage?.Author.Name is { Length: > 0 } name ? name : target.Name, 18, true));
            if (_vm.AuthorPage is { } author) { children.Children.Add(Text(author.Bio, 12, muted: true)); children.Children.Add(Text(F("{0} 关注 · {1} 粉丝 · {2} 获赞", author.Following?.ToString() ?? "—", author.Followers?.ToString() ?? "—", author.Likes?.ToString() ?? "—"), 11, muted: true)); }
            if (target.Kind == "author") children.Children.Add(Row(6, AsyncButton(T("文章"), async () => { _vm.AuthorMoments = false; await _vm.LoadAsync(); }, !_vm.AuthorMoments ? "primary" : "flat"), AsyncButton(T("动态"), async () => { _vm.AuthorMoments = true; await _vm.LoadAsync(); }, _vm.AuthorMoments ? "primary" : "flat")));
            children.Children.Add(Row(6, AsyncButton(T("在此搜索"), async () => { _vm.SearchScope = target.Kind == "author" ? null : target; _vm.Query = ""; await _vm.NavigateAsync("search"); }), AsyncButton(T("屏蔽"), async () => { var kind = target.Kind switch { "author" => RuleKind.AUTHOR, "section" => RuleKind.SECTION, _ => RuleKind.TOPIC }; AddRule(new(kind, target.Id, target.Name, Source: Source.HEYBOX)); await Task.CompletedTask; })));
        }
        if (_vm.BlockedCount > 0 || _vm.Reveal) children.Children.Add(Button(T("显示已屏蔽内容"), () => _vm.Reveal = !_vm.Reveal, "flat"));
        return children;
    }
    private Control BuildListFooter()
    {
        var stack = Stack(8); stack.Margin = new(16, 12);
        if (_vm.Error.Length > 0)
        {
            stack.Children.Add(Text(T(_vm.Error), 12)); stack.Children.Add(AsyncButton(T("重试"), () => _vm.LoadAsync(_vm.Stories.Count > 0)));
            if (_vm.Error.Contains("验证")) stack.Children.Add(Button(T("官方验证入口"), Verify));
            if (_vm.Api.Session == null && _vm.Page is "favorites" or "following" or "cloudhistory") stack.Children.Add(AsyncButton(T("登录小黑盒"), LoginDialog));
        }
        else if (_vm.Loading) stack.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 3 });
        else if (_vm.HasMore) stack.Children.Add(AsyncButton(T("加载更多"), () => _vm.LoadAsync(true)));
        else if (_vm.Stories.Count == 0) stack.Children.Add(Text(T("暂无内容"), 14, muted: true));
        return stack;
    }
    private Control BuildListSkeleton()
    {
        var stack = Stack(18); stack.Margin = new(17, 16); Avalonia.Automation.AutomationProperties.SetName(stack, T("正在加载…"));
        Border Bar(double height, double width = double.NaN) => Brush(new Border { Height = height, Width = width, CornerRadius = new(6), HorizontalAlignment = double.IsNaN(width) ? HorizontalAlignment.Stretch : HorizontalAlignment.Left }, Border.BackgroundProperty, "Subtle");
        for (var i = 0; i < 5; i++) stack.Children.Add(Columns("100,16,*", Bar(68), new Border(), Stack(12, Bar(14), Bar(12), Bar(10, 80))));
        return new ScrollViewer { Content = stack, IsHitTestVisible = false };
    }
    private Control RenderBlock(ArticleBlock block, string[] gallery)
    {
        Control control;
        switch (block.Kind)
        {
            case BlockKind.Image:
                if (_vm.Prefs.NoImages) control = Button(T("图片 · 点击查看"), () => Run(() => ImageViewer(gallery, Array.IndexOf(gallery, block.Url!))), "flat");
                else { var image = new RemoteImage(_images, block.Url, 280) { PreserveAspectRatio = true }; image.Tapped += (_, _) => Run(() => ImageViewer(gallery, Array.IndexOf(gallery, block.Url!))); control = Stack(6, image, Text(block.Caption ?? "", 11, muted: true)); }
                break;
            case BlockKind.Heading: control = Text(block.Text, block.Level <= 2 ? 20 : 18, true); control = Brush(new Border { Child = control, Padding = new(14, 0, 0, 0), BorderThickness = new(5, 0, 0, 0) }, Border.BorderBrushProperty, "Accent"); break;
            case BlockKind.Quote:
                var children = Stack(8);
                foreach (var child in block.Children ?? []) { var quoteChild = child.Kind == BlockKind.Paragraph ? child with { Runs = (child.Runs ?? [new(child.Text)]).Select(r => r with { Italic = true }).ToArray() } : child; var view = RenderBlock(quoteChild, gallery); view.Margin = default; children.Children.Add(view); }
                var quoteIcon = Icons.Get("quote", 24, "Accent"); quoteIcon.VerticalAlignment = VerticalAlignment.Top;
                control = Brush(new Border { Child = Columns("28,12,*", quoteIcon, new Border(), children), Padding = new(16, 9), CornerRadius = new(8) }, Border.BackgroundProperty, "Selection"); break;
            case BlockKind.Game:
                if (!_vm.VisualFixture && block.AppId is { } appId) { control = new GameCardView(_gameDetails, _images, appId, block.Text, _vm.Prefs.NoImages, async game => { SaveOffsets(); await _vm.NavigateAsync("news", game: game); }); break; }
                var gameButton = AsyncButton("", async () => { if (block.AppId is { } app) await _vm.NavigateAsync("news", game: new(app, block.Text, null)); else OpenUrl(block.Url); });
                gameButton.HorizontalContentAlignment = HorizontalAlignment.Stretch; gameButton.Padding = new(8, 6);
                gameButton.Content = Columns("138,14,*,20", Picture(block.Url?.StartsWith("design:") == true ? block.Url : block.AppId is { } gameId ? $"https://cdn.akamai.steamstatic.com/steam/apps/{gameId}/header.jpg" : null, 58, 138), new Border(), Stack(8, Text(block.Text.Length > 0 ? block.Text : $"Steam App {block.AppId}", 16, true), Text(block.Caption ?? "Steam", 13, muted: true)), Icons.Get("chevron_right", 18, "Muted")); control = gameButton; break;
            case BlockKind.Video: control = block.Url == null ? Text(T("视频暂不可用")) : new NativeVideo(block.Url) { Height = 300 }; break;
            case BlockKind.ExternalVideo: control = Button(block.Text, () => OpenUrl(block.Url), "flat"); break;
            default: control = RichText(block); break;
        }
        control.Margin = new(block.Kind == BlockKind.ListItem ? Math.Max(0, block.Level - 1) * 18 : 0, 0, 0, _vm.Prefs.ParagraphSpacing); return control;
    }
    private SelectableTextBlock ReadingText(string text) => Ui.Brush(new SelectableTextBlock { Text = text, FontSize = _vm.Prefs.BodySize, LineHeight = _vm.Prefs.LineHeight, TextWrapping = TextWrapping.Wrap, FontFamily = ResolveFont(_vm.Prefs.BodyFont) }, SelectableTextBlock.ForegroundProperty, "Ink");
    private static readonly Dictionary<string, string> Emoji = LoadEmoji();
    private static Dictionary<string, string> LoadEmoji()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("HeyRead.Desktop.Resources.official_emojis.json")!;
        using var doc = JsonDocument.Parse(stream); return doc.RootElement.GetProperty("entries").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
    }
    private Control RichText(ArticleBlock block)
    {
        var text = ReadingText(""); text.Inlines = new InlineCollection();
        var runs = block.Runs ?? [new RichRun(block.Text)];
        if (block.Kind == BlockKind.ListItem) runs = new[] { new RichRun((block.ListMarker ?? "•") + "  ") }.Concat(runs).ToArray();
        foreach (var run in runs)
        {
            if (run.ImageUrl != null && !_vm.Prefs.NoImages) { var image = new RemoteImage(_images, run.ImageUrl, 24) { Width = 24 }; ToolTip.SetTip(image, run.Text); text.Inlines.Add(new InlineUIContainer(image)); continue; }
            var position = 0;
            foreach (Match match in Regex.Matches(run.Text, @"\[[^\]\r\n]{1,40}\]"))
            {
                if (!Emoji.TryGetValue(match.Value, out var image) || _vm.Prefs.NoImages) continue;
                Add(run.Text[position..match.Index], run);
                var emoji = new RemoteImage(_images, image, 24) { Width = 24 }; ToolTip.SetTip(emoji, match.Value); text.Inlines.Add(new InlineUIContainer(emoji)); position = match.Index + match.Length;
            }
            Add(run.Text[position..], run);
        }
        return text;
        void Add(string value, RichRun run)
        {
            if (value.Length == 0) return;
            if (run.Url != null)
            {
                var link = Button("", () => OpenUrl(run.Url), "flat"); link.Content = Ui.Brush(Text(value, _vm.Prefs.BodySize), TextBlock.ForegroundProperty, "Accent"); link.MaxWidth = Math.Max(100, ReaderWidth - 50); link.Padding = new(0); link.FontSize = _vm.Prefs.BodySize; ToolTip.SetTip(link, run.Url); text.Inlines!.Add(new InlineUIContainer(link)); return;
            }
            text.Inlines!.Add(new Run(value) { FontWeight = run.Bold ? FontWeight.Bold : FontWeight.Normal, FontStyle = run.Italic ? FontStyle.Italic : FontStyle.Normal, FontFamily = run.Code ? new("Menlo, Monaco") : text.FontFamily });
        }
    }
    private NativeMenu? _nativeMenu;
    private readonly List<(NativeMenuItem Item, string Key)> _menuLabels = [];
    private void ConfigureMenus()
    {
        if (_nativeMenu != null) { foreach (var (item, key) in _menuLabels) item.Header = T(key); return; }
        var root = new NativeMenu(); _nativeMenu = root;
        var navigation = new NativeMenu();
        foreach (var (label, page) in new[] { ("发现", "discover"), ("推荐动态", "dynamic"), ("关注动态", "following"), ("Steam 游戏", "games"), ("云端收藏", "favorites"), ("本机历史", "history"), ("搜索记录", "search"), ("设置", "settings") }) { var item = new NativeMenuItem { Header = T(label) }; item.Click += (_, _) => Run(async () => { if (page == "search") _vm.Query = ""; await Navigate(page); }); navigation.Items.Add(item); _menuLabels.Add((item, label)); }
        root.Items.Add(new NativeMenuItem { Header = T("浏览"), Menu = navigation });
        var reading = new NativeMenu(); var original = new NativeMenuItem { Header = T("打开原文") }; original.Click += (_, _) => OpenUrl(_vm.Article?.Story.Url); reading.Items.Add(original); _menuLabels.Add((original, "打开原文")); var translate = new NativeMenuItem { Header = T("翻译为中文") }; translate.Click += (_, _) => Run(_vm.TranslateAsync); reading.Items.Add(translate); _menuLabels.Add((translate, "翻译为中文")); root.Items.Add(new NativeMenuItem { Header = T("阅读"), Menu = reading });
        foreach (var item in root.Items.OfType<NativeMenuItem>()) { if (item.Header is { } label) _menuLabels.Add((item, item.Menu == navigation ? "浏览" : "阅读")); foreach (var child in item.Menu?.Items.OfType<NativeMenuItem>() ?? []) if (!_menuLabels.Any(x => x.Item == child) && child.Header is { } childLabel) _menuLabels.Add((child, childLabel.ToString()!)); }
        var updateItem = new NativeMenuItem { Header = T("检查更新") }; updateItem.Click += (_, _) => Run(() => CheckForUpdates(true)); root.Items.Add(updateItem); _menuLabels.Add((updateItem, "检查更新"));
        NativeMenu.SetMenu(this, root);
    }
    private readonly Dictionary<string, FontFamily> _fontFamilies = [];
    private FontFamily ResolveFont(string name)
    {
        if (name == "System") return new("PingFang SC, Helvetica Neue, Arial"); if (name == "Serif") return new("Songti SC, Georgia"); if (name == "Mono") return new("Menlo, Monaco"); if (name == "Sans") return new("PingFang SC, Helvetica Neue");
        var path = Path.Combine(_vm.Store.Root, "fonts", Path.GetFileName(name));
        if (!File.Exists(path)) return new("PingFang SC, Helvetica Neue, Arial");
        if (!_fontFamilies.TryGetValue(path, out var family)) _fontFamilies[path] = family = new(Native.RegisterFont(path) ?? "PingFang SC");
        return family;
    }
    private void SwitchComments(bool value) { if (_vm.CommentsMode == value) return; SaveOffsets(); _vm.CommentsMode = value; Render(); }
    private Control BuildComments()
    {
        var stack = Stack(14); stack.Margin = new(20, 18);
        stack.Children.Add(Text(_vm.Article?.Story.Title ?? T("评论"), 24, true));
        stack.Children.Add(Row(12, Button(T("正文"), () => SwitchComments(false), "flat"), Brush(Text(T("评论") + " " + (_vm.Article?.CommentCount ?? _vm.Comments.Count), 16, true), TextBlock.ForegroundProperty, "Accent")));
        stack.Children.Add(Line());
        if (_vm.Comments.Count == 0) stack.Children.Add(Text(_vm.Api.Session == null ? "登录后查看完整评论" : T("暂无内容"), 13, muted: true));
        var gallery = _vm.Comments.SelectMany(comment => new[] { comment }.Concat(comment.Replies)).ToArray();
        foreach (var c in _vm.Comments) stack.Children.Add(CommentView(c, gallery));
        if (_vm.CommentsError.Length > 0) stack.Children.Add(Text(T(_vm.CommentsError), 13));
        if (_vm.MoreComments || _vm.CommentsError.Length > 0) stack.Children.Add(AsyncButton(T("加载更多"), _vm.MoreCommentsAsync)); return stack;
    }
    private Task OpenCommentImage(IReadOnlyList<Comment> thread, Comment selected, int index)
    {
        var offset = 0;
        foreach (var comment in thread)
        {
            if (ReferenceEquals(comment, selected)) break;
            offset += comment.Images.Count;
        }
        return ImageViewer(thread.SelectMany(comment => comment.Images).ToArray(), offset + index);
    }
    private Control CommentView(Comment c, IReadOnlyList<Comment> gallery, bool reply = false)
    {
        var name = Button(c.Author.Name, () => { if (c.Author.Id.Length > 0) Run(() => _vm.NavigateAsync("destination", new("author", c.Author.Id, c.Author.Name))); }, "flat"); name.Padding = new(0);
        var body = Stack(8, Row(8, name, Text(c.Date, 11, muted: true)), RichText(new(BlockKind.Paragraph, c.Text)));
        for (var i = 0; i < c.Images.Count; i++)
        {
            var index = i;
            if (_vm.Prefs.NoImages)
                body.Children.Add(AsyncButton(T("图片 · 点击查看"), () => OpenCommentImage(gallery, c, index), "flat"));
            else
            {
                var image = new RemoteImage(_images, c.Images[i], 150);
                image.Tapped += (_, _) => Run(() => OpenCommentImage(gallery, c, index));
                body.Children.Add(image);
            }
        }
        var supported = c.Supported; var supports = c.Supports; Button? support = null;
        support = AsyncButton($"{(supported ? "✓" : "↑")} {supports}", async () => { if (_vm.Api.Session == null) { await LoginDialog(); if (_vm.Api.Session == null) return; } try { await _vm.SocialAsync("support", c with { Supported = supported, Supports = supports }); supported = !supported; supports = Math.Max(0, supports + (supported ? 1 : -1)); support!.Content = $"{(supported ? "✓" : "↑")} {supports}"; } catch (Exception e) { await Notice(e.Message); } }, "flat");
        body.Children.Add(Row(8, AsyncButton(T("回复"), () => CommentDialog(c), "flat"), support));
        if (!reply) { foreach (var child in c.Replies) body.Children.Add(CommentView(child, gallery, true)); body.Children.Add(AsyncButton(T("查看楼中楼"), async () => { await RepliesDialog(c); }, "flat")); }
        var border = Brush(new Border { Child = Columns("32,12,*", Avatar(_vm.Prefs.NoImages ? null : c.Author.Avatar, 32), new Border(), body), Padding = new(reply ? 16 : 0, 0, 0, 18), BorderThickness = new(0, 0, 0, 1) }, Border.BorderBrushProperty, "Line"); return border;
    }
    private Control EmptyReader() => new Border { Child = Stack(12, Icons.Get("book_open", 40), Text(T("选择一篇文章开始阅读"), 16, muted: true)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private CancellationTokenSource? _navigationMotion;
    private void AnimateNavigation()
    {
        _navigationMotion?.Cancel(); _navigationMotion?.Dispose(); _navigationMotion = null;
        _body.Transitions = null; _body.Opacity = 1;
        if (!IsVisible || !_vm.Prefs.Animations || OperatingSystem.IsMacOS() && Native.hr_reduce_motion() != 0) return;
        _navigationMotion = new();
        var animation = new Animation { Duration = TimeSpan.FromMilliseconds(120), Easing = new CubicEaseOut() };
        animation.Children.Add(new KeyFrame { Cue = new Cue(0), Setters = { new Avalonia.Styling.Setter(OpacityProperty, 0d) } });
        animation.Children.Add(new KeyFrame { Cue = new Cue(1), Setters = { new Avalonia.Styling.Setter(OpacityProperty, 1d) } });
        _ = FinishNavigationMotion(animation, _navigationMotion.Token);
    }
    private async Task FinishNavigationMotion(Animation animation, CancellationToken token)
    {
        try { await animation.RunAsync(_body, token); }
        catch (OperationCanceledException) { }
        finally { if (!token.IsCancellationRequested) _body.Opacity = 1; }
    }
    private void ApplyTheme()
    {
        if (Application.Current is not { } app) return;
        Native.SetLanguage(_vm.Prefs.Language);
        app.RequestedThemeVariant = _vm.Prefs.Theme switch { "Dark" => ThemeVariant.Dark, "Light" => ThemeVariant.Light, _ => ThemeVariant.Default };
        var accent = Color.Parse(_vm.Prefs.Accent switch { "Yellow" => "#AD6900", "Green" => "#218455", "Purple" => "#8355CB", "Blue" => "#2563EB", _ => Native.Accent() });
        app.Resources["AccentFill"] = new SolidColorBrush(accent);
        var luminance = (.2126 * Linear(accent.R) + .7152 * Linear(accent.G) + .0722 * Linear(accent.B));
        app.Resources["OnAccent"] = luminance > .179 ? Brushes.Black : Brushes.White;
        var ink = app.ActualThemeVariant == ThemeVariant.Dark ? Color.FromRgb((byte)(accent.R * .55 + 255 * .45), (byte)(accent.G * .55 + 255 * .45), (byte)(accent.B * .55 + 255 * .45)) : accent;
        app.Resources["Accent"] = new SolidColorBrush(ink);
        FontFamily = ResolveFont(_vm.Prefs.UiFont);
        if (!_vm.Prefs.Animations) { _navigationMotion?.Cancel(); _body.Opacity = 1; }
    }
    private static double Linear(byte channel) { var value = channel / 255.0; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
    private void OnSystemColorsChanged(object? sender, Avalonia.Platform.PlatformColorValues e) => Dispatcher.UIThread.Post(ApplyTheme);
    private bool IsDirectoryPage => _vm.Page is "dynamic" or "following" || _vm.Page == "destination" && _vm.Target?.Kind == "author";
    private bool DirectoryOverview => IsDirectoryPage && !_showDetail;
    private bool _overviewRows;
    private void ReturnToList()
    {
        SaveOffsets();
        _showDetail = false;
        _list.SelectedItem = null;
        Render();
    }
    private void UpdateResponsive()
    {
        _narrow = Bounds.Width < 1120;
        if (_vm.Page is "settings" or "account" or "games") return;
        if (_readingGrid.Children.Count < 3) return;
        if (DirectoryOverview)
        {
            _readingGrid.ColumnDefinitions[0].Width = new(1, GridUnitType.Star);
            _readingGrid.ColumnDefinitions[1].Width = new(0);
            _readingGrid.ColumnDefinitions[2].Width = new(0);
            _readingGrid.Children[0].IsVisible = true;
            _readingGrid.Children[1].IsVisible = false;
            _detail.IsVisible = false;
            return;
        }
        var list = _readingGrid.Children[0]; var splitter = _readingGrid.Children[1];
        list.IsVisible = !_narrow || !_showDetail; splitter.IsVisible = !_narrow; _detail.IsVisible = !_narrow || _showDetail;
        _readingGrid.ColumnDefinitions[0].Width = _narrow ? _showDetail ? new(0) : new(1, GridUnitType.Star) : new(Math.Clamp(_vm.Prefs.ListWidth, 280, 480));
        _readingGrid.ColumnDefinitions[1].Width = new(_narrow ? 0 : 1); _readingGrid.ColumnDefinitions[2].Width = _narrow && !_showDetail ? new(0) : new(1, GridUnitType.Star);
    }
    private static void Detach(Control control) { if (control.Parent is Panel p) p.Children.Remove(control); else if (control.Parent is ContentControl c) c.Content = null; else if (control.Parent is Border b) b.Child = null; }
    private WindowNotificationManager? _notifications;
    private ScrollViewer? _listScroll;
    private DispatcherTimer? _viewportTimer;
    private string? _visiblePageKey;
    private double? _pendingListOffset;
    private void TrackListScroll()
    {
        var scroll = _list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(); if (scroll == _listScroll) return;
        if (_listScroll != null) _listScroll.ScrollChanged -= OnListScrollChanged; _listScroll = scroll;
        if (_listScroll != null) _listScroll.ScrollChanged += OnListScrollChanged;
    }
    private void OnListScrollChanged(object? sender, ScrollChangedEventArgs e) { if (!_rendering && !_vm.Loading) ScheduleViewportSave(); }
    private void ScheduleViewportSave()
    {
        if (_viewportTimer == null) { _viewportTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) }; _viewportTimer.Tick += (_, _) => { _viewportTimer.Stop(); SaveOffsets(); }; }
        _viewportTimer.Stop(); _viewportTimer.Start();
    }
    private void SaveOffsets() => _vm.SaveViewport(_listScroll?.Offset.Y ?? _vm.CurrentViewport.ListOffset, _articleOffset, _commentOffset, _showDetail);
    private void OnSettingsChanged()
    {
        _strings = new(_vm.Prefs.Language); ApplyTheme(); ConfigureMenus(); Detach(_search); _header.Content = BuildHeader(); RenderSidebar(); _list.ItemTemplate = new FuncDataTemplate<Story>((s, _) => StoryRow(s));
    }
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_vm.Toast) && _vm.Toast.Length > 0) _notifications?.Show(new Notification(ProductInfo.DisplayName, T(_vm.Toast), NotificationType.Information, TimeSpan.FromSeconds(4)));
        if (e.PropertyName is nameof(_vm.Loading) or nameof(_vm.DetailLoading) or nameof(_vm.CommentsMode) or nameof(_vm.Translating)) Render();
    }
    private async Task Social(string action, Comment? comment = null, string? expectedStory = null) { if (_vm.VisualFixture) { _vm.Toast = "离线样例不执行账号操作"; return; } if (_vm.Api.Session == null) { await LoginDialog(); if (_vm.Api.Session == null) return; } if (expectedStory != null && _vm.Article?.Story.Key != expectedStory) return; try { await _vm.SocialAsync(action, comment); } catch (Exception e) { await Notice(e.Message); } }
    private async void Run(Func<Task> action) { try { await action(); } catch (OperationCanceledException) { } catch (Exception e) { Console.Error.WriteLine(e); _status.Text = e.Message; await Notice(e.Message); } }
    private void OpenUrl(string? url) { if (J.Https(url) != null) Run(async () => { await Launcher.LaunchUriAsync(new Uri(url!)); }); }
    private void Verify() => Native.Verify(_vm.Article?.Story.Url ?? "https://www.xiaoheihe.cn/");
    private void OnKeys(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) { switch (e.Key) { case Key.W: Close(); e.Handled = true; break; case Key.F: _search.Focus(); e.Handled = true; break; case Key.R: Run(() => _vm.LoadAsync()); e.Handled = true; break; case Key.OemComma: Run(() => Navigate("settings")); e.Handled = true; break; case Key.Left: Run(async () => { SaveOffsets(); await _vm.BackAsync(); }); e.Handled = true; break; case Key.Right: Run(async () => { SaveOffsets(); await _vm.BackAsync(true); }); e.Handled = true; break; } }
        if (e.Key == Key.Escape && (_narrow || IsDirectoryPage) && _showDetail) { ReturnToList(); e.Handled = true; }
    }
}
