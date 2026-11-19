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
    private const double DockClearance = 78;
    private bool HasReader => ReferenceEquals(_body.Content, _readingGrid) && _vm.Article != null;
    private readonly MainViewModel _vm;
    private readonly ImageService _images;
    private readonly GameDetailsService _gameDetails;
    private Strings _strings;
    private string T(string key) => _strings[key];
    private string F(string key, params object?[] args) => string.Format(System.Globalization.CultureInfo.CurrentCulture, T(key), args);
    private readonly AReferenceAssets _referenceAssets = new();
    private readonly Grid _root = new();
    private readonly ContentControl _body = new();
    private readonly ContentControl _detail = new();
    private readonly Border _readerPane = new() { CornerRadius = new(11), ClipToBounds = true };
    private readonly TextBlock _status = Text("", 12, muted: true);
    private TextBox? _pageSearch;
    private readonly ListBox _list = new();
    private readonly Grid _readingGrid = new();
    private readonly ContentControl _listHeader = new();
    private readonly ContentControl _listFooter = new();
    private readonly ContentControl _listLoading = new() { IsHitTestVisible = false };
    private readonly ScrollViewer _readerScroll = new() { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    private bool _rendering;
    private string? _renderedKey;
    private double _articleOffset;
    public MainWindow(MainViewModel vm)
    {
        _vm = vm; DataContext = vm; _strings = new(vm.Prefs.Language); _images = new(Path.Combine(vm.Store.Root, "cache")); _gameDetails = new(vm.Api);
        Title = _vm.VisualFixture ? ProductInfo.DisplayName + " — A 视觉样例" : ProductInfo.DisplayName; Width = CompactWindowWidth; Height = 730; MinWidth = MinimumCompactWidth; MinHeight = 520;
        if (_vm.VisualFixture && Environment.GetEnvironmentVariable("HEYREAD_VISUAL_VIEWPORT") == "narrow") Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; PlaceCompactWindow(); ExtendClientAreaToDecorationsHint = true; ExtendClientAreaTitleBarHeightHint = 30;
        ApplyRendering(this);
        _root.RowDefinitions = new("30,*");
        Content = _root;
        var dragStrip = Brush(new Border(), Border.BackgroundProperty, "Frame");
        dragStrip.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };
        _root.Children.Add(dragStrip);
        Grid.SetRow(_body, 1);
        _root.Children.Add(_body);
        Grid.SetRow(_dockAnchor, 1);
        _root.Children.Add(_dockAnchor);
        BuildDock();
        _list.ItemsSource = vm.Stories;
        _list.ItemTemplate = new FuncDataTemplate<Story>((story, _) => StoryRow(story));
        _list.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel { Margin = new(0, 0, 0, DockClearance) });
        MountReadingGrid();
        _list.SelectionChanged += async (_, _) =>
        {
            if (!_rendering && _list.SelectedItem is Story story) await OpenStoryAsync(story);
        };
        _readerScroll.ScrollChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.Source, _readerScroll) || _rendering || vm.Article == null || !_readerScroll.IsEffectivelyVisible) return;
            if (_pendingReaderOffset == null && _pendingCommentOffset == null && !_vm.DetailLoading)
            {
                _articleOffset = _readerScroll.Offset.Y;
                ScheduleViewportSave();
            }
            QueuePagination();
        };
        vm.ContentChanged += Render;
        vm.SettingsChanged += OnSettingsChanged;
        vm.PropertyChanged += OnViewModelChanged;
        SizeChanged += (_, e) =>
        {
            UpdateResponsive();

            UpdateDock();
            if (!_rendering && HasReader) RenderReader();
        }; KeyDown += OnKeys;
        if (Application.Current?.PlatformSettings is { } settings) settings.ColorValuesChanged += OnSystemColorsChanged;
        Opened += async (_, _) => { _notifications = new WindowNotificationManager(this) { MaxItems = 3, Position = NotificationPosition.BottomRight }; ApplyTheme(); _ = CheckAfterStartup(); await vm.InitializeAsync(); };
        Closing += (_, _) => { SaveOffsets(); vm.Store.SavePreferences(); };
        Closed += (_, _) => { _updateLifetime.Cancel(); _updates.Dispose(); _viewportTimer?.Stop(); _pullTimer.Stop(); _navigationMotion?.Cancel(); _navigationMotion?.Dispose(); if (Application.Current?.PlatformSettings is { } settings) settings.ColorValuesChanged -= OnSystemColorsChanged; if (_listScroll != null) _listScroll.ScrollChanged -= OnListScrollChanged; vm.SettingsChanged -= OnSettingsChanged; vm.PropertyChanged -= OnViewModelChanged; vm.ContentChanged -= Render; _readerScroll.Content = null; vm.Suspend(); _images.Dispose(); _referenceAssets.Dispose(); };
        ConfigureMenus(); ApplyTheme(); Render();
    }
    private static void ApplyRendering(Window window)
    {
        window.UseLayoutRounding = true;
        RenderOptions.SetEdgeMode(window, EdgeMode.Antialias);
        TextOptions.SetTextRenderingMode(window, TextRenderingMode.Antialias);
        TextOptions.SetBaselinePixelAlignment(window, BaselinePixelAlignment.Aligned);
    }
    private Task Navigate(string page, bool refresh = false)
    {
        SaveOffsets();
        _vm.SearchScope = null;
        return _vm.NavigateAsync(page, refresh: refresh);
    }

    private Task OpenStoryAsync(Story story)
    {
        SaveOffsets();
        if (_vm.Article == null) _pendingListOffset ??= _listScroll?.Offset.Y;
        return _vm.SelectAsync(story);
    }

    private void MountReadingGrid()
    {
        var listPanel = new Grid { RowDefinitions = new("Auto,*") };
        listPanel.Children.Add(_listHeader);
        var viewport = BuildListViewport();
        Grid.SetRow(viewport, 1);
        listPanel.Children.Add(viewport);
        _listFooter.VerticalAlignment = VerticalAlignment.Bottom;
        _listFooter.Margin = new(0, 0, 0, DockClearance);
        Grid.SetRow(_listFooter, 1);
        listPanel.Children.Add(_listFooter);
        var border = Brush(new Border { Child = listPanel, CornerRadius = new(11), ClipToBounds = true }, Border.BackgroundProperty, "FeedSurface");
        border.SizeChanged += (_, _) => UpdateDock();
        _readingGrid.ColumnDefinitions = new("*,0,0");
        _readingGrid.Margin = new(PaneInset);
        _readingGrid.Children.Add(border);
        _readerPane.Child = _detail;
        Brush(_readerPane, Border.BackgroundProperty, "Surface");
        Grid.SetColumn(_readerPane, 2);
        _readingGrid.Children.Add(_readerPane);
    }
    private void Render()
    {
        if (_rendering) return; _rendering = true;
        try
        {
            var key = _vm.CurrentViewport.CacheKey;
            if (_visiblePageKey != key) { _visiblePageKey = key; _renderedKey = null; _pendingListOffset = _vm.CurrentViewport.ListOffset; _pagesWithoutVisibleRows = 0; ResetListPull(); AnimateNavigation(); }
            _pageSearch = null;
            if (_vm.Page == "settings") { _body.Content = BuildSettings(); return; }
            if (_vm.Page == "account") { _body.Content = BuildAccount(); return; }
            if (_vm.Page == "search" && string.IsNullOrWhiteSpace(_vm.CurrentViewport.Query)) { _body.Content = BuildSearchLanding(); return; }
            if (_vm.Page == "games") { _body.Content = BuildGames(); return; }
            _body.Content = _readingGrid;
            if (_overviewRows != DirectoryOverview)
            {
                _overviewRows = DirectoryOverview;
                _list.ItemTemplate = new FuncDataTemplate<Story>((story, _) => StoryRow(story));
            }
            _listHeader.Content = BuildListHeader();
            _listFooter.Content = BuildListFooter();
            _listLoading.Content = _vm.Loading && _vm.Stories.Count == 0 ? _listLoading.Content ?? BuildListSkeleton() : null;
            var selected = _vm.Article?.Story.Key;
            _list.SelectedItem = selected == null ? null : _vm.Stories.FirstOrDefault(story => story.Key == selected);
            UpdateResponsive();
            RenderReader();
            _status.Text = _vm.Loading ? T("正在加载…") : _vm.Demo ? "离线演示内容 · 不代表在线资讯" : $"{_vm.Stories.Count} 篇内容" + (_vm.BlockedCount > 0 ? $" · 已屏蔽 {_vm.BlockedCount} 篇" : "");
        }
        finally
        {
            _rendering = false;
            UpdateResponsive();
            _body.Margin = ReferenceEquals(_body.Content, _readingGrid) ? default : new(4, 4, 4, DockClearance);
            UpdateDock();
            QueuePagination();
        }
    }
    private Control? BuildListHeader()
    {
        var children = Stack(8); children.Margin = new(12, 12, 12, 4);
        if (_vm.Page is not ("discover" or "destination" or "search" or "dynamic" or "following"))
        {
            var title = Text(T(_vm.Title), 16, true);
            var count = Text(_vm.Stories.Count.ToString(), 11, muted: true);
            count.VerticalAlignment = VerticalAlignment.Center;
            children.Children.Add(Columns("*,Auto", title, count));
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
            children.Margin = new(12, 10, 12, 4);
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
            var search = _vm.CurrentViewport;
            children.Children.Add(Text(search.SearchScope == null ? $"“{search.Query}”" : $"{search.SearchScope.Name} · {search.Query}", 14, true));
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
        return children.Children.Count == 0 ? null : children;
    }
    private Control BuildListFooter()
    {
        var stack = Stack(5);
        if (_vm.Error.Length > 0)
        {
            stack.Children.Add(Text(T(_vm.Error), 12)); stack.Children.Add(AsyncButton(T("重试"), () => _vm.LoadAsync(_vm.HasMore)));
            if (_vm.Error.Contains("验证")) stack.Children.Add(Button(T("官方验证入口"), Verify));
            if (_vm.Api.Session == null && _vm.Page is "favorites" or "following" or "cloudhistory") stack.Children.Add(AsyncButton(T("登录小黑盒"), LoginDialog));
        }
        else if (_vm.Loading && _vm.Stories.Count > 0) stack.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 2 });
        else if (!_vm.Loading && _vm.Stories.Count == 0) stack.Children.Add(Text(T("暂无内容"), 13, muted: true));
        if (stack.Children.Count > 0) stack.Margin = new(12, 6);
        return stack;
    }
    private Control BuildListSkeleton()
    {
        var stack = Stack(10); stack.Margin = new(12, 8); Avalonia.Automation.AutomationProperties.SetName(stack, T("正在加载…"));
        Border Bar(double height, double width = double.NaN) => Brush(new Border { Height = height, Width = width, CornerRadius = new(6), HorizontalAlignment = double.IsNaN(width) ? HorizontalAlignment.Stretch : HorizontalAlignment.Left }, Border.BackgroundProperty, "Subtle");
        for (var i = 0; i < 8; i++) stack.Children.Add(Columns("68,10,*", Bar(46), new Border(), Stack(6, Bar(12), Bar(10), Bar(9, 80))));
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
    private static readonly Dictionary<string, string> Emoji = LoadEmoji();
    private static readonly Regex EmojiToken = new(@"\[[^\]\r\n]{1,40}\]", RegexOptions.Compiled);
    private static Dictionary<string, string> LoadEmoji()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("HeyRead.Desktop.Resources.official_emojis.json")!;
        using var doc = JsonDocument.Parse(stream); return doc.RootElement.GetProperty("entries").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
    }
    private SelectableTextBlock ReadingText(string text, double? size = null, double? lineHeight = null) =>
        Brush(new SelectableTextBlock
        {
            Text = text, FontSize = size ?? _vm.Prefs.BodySize, LineHeight = lineHeight ?? _vm.Prefs.LineHeight,
            TextWrapping = TextWrapping.Wrap, FontFamily = ResolveFont(_vm.Prefs.BodyFont)
        }, SelectableTextBlock.ForegroundProperty, "Ink");

    private SelectableTextBlock RichText(ArticleBlock block, double? fontSize = null, double? lineHeight = null)
    {
        var size = fontSize ?? _vm.Prefs.BodySize;
        var text = ReadingText("", size, lineHeight);
        var runs = block.Runs ?? [new RichRun(block.Text)];
        if (block.Kind == BlockKind.ListItem) runs = new[] { new RichRun((block.ListMarker ?? "•") + "  ") }.Concat(runs).ToArray();
        var emojiSize = fontSize.HasValue ? size + 2 : size;
        PopulateInlines(text, runs, emojiSize);
        return text;
    }

    private void PopulateInlines(TextBlock text, IReadOnlyList<RichRun> runs, double emojiSize)
    {
        var inlines = new InlineCollection();
        text.Inlines = inlines;
        foreach (var run in runs)
        {
            if (run.ImageUrl != null && !_vm.Prefs.NoImages) { AddEmoji(run.ImageUrl, run.Text); continue; }
            var position = 0;
            foreach (Match match in EmojiToken.Matches(run.Text))
            {
                if (!Emoji.TryGetValue(match.Value, out var image) || _vm.Prefs.NoImages) continue;
                Add(run.Text[position..match.Index], run);
                AddEmoji(image, match.Value);
                position = match.Index + match.Length;
            }
            Add(run.Text[position..], run);
        }
        void AddEmoji(string url, string label)
        {
            var image = new RemoteImage(_images, url, emojiSize) { Width = emojiSize };
            ToolTip.SetTip(image, label);
            inlines.Add(new InlineUIContainer(image) { BaselineAlignment = BaselineAlignment.Center });
        }

        void Add(string value, RichRun run)
        {
            if (value.Length == 0) return;
            if (run.Url != null)
            {
                var link = Button("", () => OpenUrl(run.Url), "flat");
                link.Content = Ui.Brush(Text(value, text.FontSize), TextBlock.ForegroundProperty, "Accent");
                link.MaxWidth = Math.Max(100, ReaderWidth - 64);
                link.Padding = default;
                link.MinHeight = 0;
                link.FontSize = text.FontSize;
                ToolTip.SetTip(link, run.Url);
                inlines.Add(new InlineUIContainer(link) { BaselineAlignment = BaselineAlignment.Center });
                return;
            }
            inlines.Add(new Run(value) { FontWeight = run.Bold ? FontWeight.Bold : text.FontWeight, FontStyle = run.Italic ? FontStyle.Italic : FontStyle.Normal, FontFamily = run.Code ? new("Menlo, Monaco") : text.FontFamily });
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
        if (name == "System") return (FontFamily)this.FindResource("DefaultUiFont")!; if (name == "Serif") return new("Songti SC, Georgia"); if (name == "Mono") return new("Menlo, Monaco"); if (name == "Sans") return new("PingFang SC, Helvetica Neue");
        var path = Path.Combine(_vm.Store.Root, "fonts", Path.GetFileName(name));
        if (!File.Exists(path)) return (FontFamily)this.FindResource("DefaultUiFont")!;
        if (!_fontFamilies.TryGetValue(path, out var family)) _fontFamilies[path] = family = new(Native.RegisterFont(path) ?? "PingFang SC");
        return family;
    }
    private CancellationTokenSource? _navigationMotion;
    private bool AllowMotion => _vm.Prefs.Animations && (!OperatingSystem.IsMacOS() || Native.hr_reduce_motion() == 0);

    private void AnimateNavigation()
    {
        _navigationMotion?.Cancel(); _navigationMotion?.Dispose(); _navigationMotion = null;
        _body.Transitions = null; _body.Opacity = 1;
        if (!IsVisible || !AllowMotion) return;
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
        app.Resources["ActiveButtonFont"] = _vm.Prefs.UiFont == "System" ? app.FindResource("DefaultButtonFont") : FontFamily;
        if (!_vm.Prefs.Animations) { _navigationMotion?.Cancel(); _body.Opacity = 1; }
    }
    private static double Linear(byte channel) { var value = channel / 255.0; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
    private void OnSystemColorsChanged(object? sender, Avalonia.Platform.PlatformColorValues e) => Dispatcher.UIThread.Post(ApplyTheme);
    private bool IsDirectoryPage => _vm.Page is "dynamic" or "following" || _vm.Page == "destination" && _vm.Target?.Kind == "author";
    private bool DirectoryOverview => IsDirectoryPage;
    private bool _overviewRows;

    private void CloseReader()
    {
        SaveOffsets();
        _pendingListOffset ??= _listScroll?.Offset.Y;
        _vm.CloseArticle();
    }

    private void UpdateResponsive()
    {
        var split = HasReader;
        MatchWindowToReader(split);
        _readerPane.IsVisible = split;
        _readingGrid.ColumnDefinitions[0].Width = split ? new(FeedColumnWidth) : new(1, GridUnitType.Star);
        _readingGrid.ColumnDefinitions[1].Width = new(split ? PaneGap : 0);
        _readingGrid.ColumnDefinitions[2].Width = split ? new(1, GridUnitType.Star) : new(0);
    }

    private WindowNotificationManager? _notifications;
    private ScrollViewer? _listScroll;
    private DispatcherTimer? _viewportTimer;
    private string? _visiblePageKey;
    private double? _pendingListOffset;
    private bool _paginationQueued;
    private int _pagesWithoutVisibleRows;
    private void TrackListScroll()
    {
        var scroll = _list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(); if (scroll == _listScroll) return;
        if (_listScroll != null) _listScroll.ScrollChanged -= OnListScrollChanged; _listScroll = scroll;
        if (_listScroll != null) _listScroll.ScrollChanged += OnListScrollChanged;
    }
    private void OnListScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, _listScroll) || _rendering) return;
        if (!_vm.Loading && _pendingListOffset == null && e.OffsetDelta.Y != 0) ScheduleViewportSave();
        QueuePagination();
    }
    private void QueuePagination()
    {
        if (_paginationQueued) return;
        _paginationQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _paginationQueued = false;
            if (!IsVisible || !ReferenceEquals(_body.Content, _readingGrid)) return;
            TrackListScroll();
            if (_listScroll is { IsEffectivelyVisible: true } list && !_vm.Loading)
            {
                if (_pendingListOffset is { } offset && list.Viewport.Height > 0)
                {
                    _pendingListOffset = null;
                    list.Offset = new(0, offset);
                }
                if (_pullDistance == 0 && _vm.HasMore && _vm.Error.Length == 0 && _pagesWithoutVisibleRows < 4 && NearEnd(list))
                {
                    var count = _vm.Stories.Count;
                    var last = _vm.Stories.LastOrDefault()?.Key;
                    var page = _visiblePageKey;
                    Run(async () =>
                    {
                        await _vm.LoadAsync(true);
                        if (page == _visiblePageKey)
                            _pagesWithoutVisibleRows = count == _vm.Stories.Count && last == _vm.Stories.LastOrDefault()?.Key
                                ? _pagesWithoutVisibleRows + 1 : 0;
                    });
                }
            }
            RestoreReaderOffset();
            if (_commentsSection.Content != null && _readerScroll.IsEffectivelyVisible && _vm.MoreComments &&
                !_vm.CommentsLoading && _vm.CommentsError.Length == 0 && NearEnd(_readerScroll))
                Run(_vm.MoreCommentsAsync);
            LoadRepliesNearViewport();
        }, DispatcherPriority.Background);
    }
    private static bool NearEnd(ScrollViewer scroll) =>
        scroll.Viewport.Height > 0 && scroll.Extent.Height - scroll.Offset.Y - scroll.Viewport.Height <= 136;
    private void ScheduleViewportSave()
    {
        if (_viewportTimer == null) { _viewportTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) }; _viewportTimer.Tick += (_, _) => { _viewportTimer.Stop(); SaveOffsets(); }; }
        _viewportTimer.Stop(); _viewportTimer.Start();
    }
    private void SaveOffsets()
    {
        if (_pendingCommentOffset != null || _pendingReaderOffset != null) return;
        var listOffset = _pendingListOffset ?? _listScroll?.Offset.Y ?? _vm.CurrentViewport.ListOffset;
        _vm.SaveViewport(listOffset, _articleOffset, 0, HasReader);
    }
    private void OnSettingsChanged()
    {
        _strings = new(_vm.Prefs.Language); ApplyTheme(); ConfigureMenus(); BuildDock(); _list.ItemTemplate = new FuncDataTemplate<Story>((story, _) => StoryRow(story));
    }
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_vm.Toast) && _vm.Toast.Length > 0) _notifications?.Show(new Notification(ProductInfo.DisplayName, T(_vm.Toast), NotificationType.Information, TimeSpan.FromSeconds(4)));
        if (e.PropertyName is nameof(_vm.Loading) or nameof(_vm.DetailLoading) or nameof(_vm.Translating)) Render();
    }
    private async Task Social(string action, Comment? comment = null, string? expectedStory = null) { if (_vm.VisualFixture) { _vm.Toast = "离线样例不执行账号操作"; return; } if (_vm.Api.Session == null) { await LoginDialog(); if (_vm.Api.Session == null) return; } if (expectedStory != null && _vm.Article?.Story.Key != expectedStory) return; try { await _vm.SocialAsync(action, comment); } catch (Exception e) { await Notice(e.Message); } }
    private async void Run(Func<Task> action) { try { await action(); } catch (OperationCanceledException) { } catch (Exception e) { Console.Error.WriteLine(e); _status.Text = e.Message; await Notice(e.Message); } }
    private void OpenUrl(string? url) { if (J.Https(url) != null) Run(async () => { await Launcher.LaunchUriAsync(new Uri(url!)); }); }
    private void Verify() => Native.Verify(_vm.Article?.Story.Url ?? "https://www.xiaoheihe.cn/");
    private void OnKeys(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) { switch (e.Key) { case Key.W: Close(); e.Handled = true; break; case Key.F: if (_pageSearch is { IsEffectivelyVisible: true } search) search.Focus(); else Run(OpenDockSearchAsync); e.Handled = true; break; case Key.R: Run(RefreshDockPageAsync); e.Handled = true; break; case Key.OemComma: Run(() => Navigate("settings")); e.Handled = true; break; case Key.Left: Run(async () => { SaveOffsets(); await _vm.BackAsync(); }); e.Handled = true; break; case Key.Right: Run(async () => { SaveOffsets(); await _vm.BackAsync(true); }); e.Handled = true; break; } }
        if (e.Key == Key.Escape && HasReader) { CloseReader(); e.Handled = true; }
    }
}
