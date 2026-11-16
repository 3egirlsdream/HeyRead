using Avalonia.Input.Platform;
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
    private Strings _strings;
    private string T(string key) => _strings[key];
    private readonly Grid _root = new();
    private readonly ContentControl _body = new();
    private readonly ContentControl _sidebar = new();
    private readonly ContentControl _detail = new();
    private readonly TextBlock _title = Text("发现", 17, true);
    private readonly TextBlock _status = Text("", 12, muted: true);
    private readonly TextBox _search = new() { MaxLength = 120, MinWidth = 190, MaxWidth = 430, HorizontalAlignment = HorizontalAlignment.Stretch };
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
        _vm = vm; DataContext = vm; _strings = new(vm.Prefs.Language); _images = new(Path.Combine(vm.Store.Root, "cache"));
        Title = "盒阅 · HeyRead"; Width = 1280; Height = 800; MinWidth = 900; MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; ExtendClientAreaToDecorationsHint = true; ExtendClientAreaTitleBarHeightHint = 48;
        _root.ColumnDefinitions = new("200,*"); Content = _root; _root.Children.Add(_sidebar);
        var main = new Grid { RowDefinitions = new("64,*,28") }; Grid.SetColumn(main, 1); _root.Children.Add(main);
        var header = BuildHeader(); main.Children.Add(header); Grid.SetRow(_body, 1); main.Children.Add(_body);
        var statusBorder = Brush(new Border { Child = _status, Padding = new Thickness(20, 4), BorderThickness = new Thickness(0, 1, 0, 0) }, Border.BorderBrushProperty, "Line"); Grid.SetRow(statusBorder, 2); main.Children.Add(statusBorder);
        _list.ItemsSource = vm.Stories; _list.ItemTemplate = new FuncDataTemplate<Story>((s, _) => StoryRow(s!));
        _list.SelectionChanged += async (_, _) => { if (_list.SelectedItem is Story story) { SaveOffsets(); _showDetail = true; await vm.SelectAsync(story); UpdateResponsive(); } };
        _readerScroll.ScrollChanged += (_, _) => { if (_rendering || vm.Article == null) return; if (vm.CommentsMode) _commentOffset = _readerScroll.Offset.Y; else _articleOffset = _readerScroll.Offset.Y; };
        _search.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { vm.Query = _search.Text ?? ""; _showDetail = false; await vm.SearchAsync(); e.Handled = true; } };
        _search.TextChanged += (_, _) => vm.Query = _search.Text ?? "";
        vm.ContentChanged += Render;
        vm.SettingsChanged += () => { ApplyTheme(); _strings = new(vm.Prefs.Language); ConfigureMenus(); _search.PlaceholderText = T("搜索内容、作者或话题"); RenderSidebar(); _list.ItemTemplate = new FuncDataTemplate<Story>((s, _) => StoryRow(s!)); };
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Toast) && vm.Toast.Length > 0) _status.Text = vm.Toast; if (e.PropertyName is nameof(vm.Loading) or nameof(vm.DetailLoading) or nameof(vm.CommentsMode) or nameof(vm.Translating)) Render(); };
        SizeChanged += (_, _) => UpdateResponsive(); KeyDown += OnKeys;
        Opened += async (_, _) => { ApplyTheme(); await vm.InitializeAsync(); };
        Closing += (_, _) => { SaveOffsets(); vm.Store.SavePreferences(); };
        Closed += (_, _) => { vm.ContentChanged -= Render; _images.Dispose(); };
        ConfigureMenus(); ApplyTheme(); RenderSidebar(); Render();
    }
    private Control BuildHeader()
    {
        var back = Icons.Button("arrow_left", T("返回"), async () => { if (_narrow && _showDetail) { SaveOffsets(); _showDetail = false; UpdateResponsive(); } else await _vm.BackAsync(); });
        var forward = Icons.Button("arrow_right", T("前进"), () => _vm.BackAsync(true));
        var refresh = Icons.Button("arrow_clockwise", T("刷新"), () => _vm.LoadAsync());
        _search.PlaceholderText = T("搜索内容、作者或话题"); _search.Text = _vm.Query;
        var grid = new Grid { ColumnDefinitions = new("Auto,*,Auto"), Margin = new Thickness(16, 14, 20, 12), ColumnSpacing = 18 };
        grid.Children.Add(Row(3, back, forward, _title)); Grid.SetColumn(_search, 1); grid.Children.Add(_search);
        var actions = Row(4, refresh, Icons.Button("text_font", T("阅读排版与字体"), async () => { _settingsTab = "reading"; await Navigate("settings"); })); Grid.SetColumn(actions, 2); grid.Children.Add(actions);
        var border = Brush(new Border { Child = grid, BorderThickness = new Thickness(0, 0, 0, 1) }, Border.BorderBrushProperty, "Line");
        border.PointerPressed += (_, e) => { if (e.Source == border && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        return border;
    }
    private void RenderSidebar()
    {
        var brand = Row(10, Ui.Brush(new Border { Child = Icons.Get("book_open", 24), CornerRadius = new(8), Padding = new(7) }, Border.BackgroundProperty, "Selection"), Stack(0, Text("盒阅", 19, true), Text("HeyRead", 10, muted: true))); brand.Margin = new(20, 52, 16, 20);
        var nav = Stack(4); nav.Margin = new(12, 0);
        foreach (var (page, label, icon) in new[] { ("discover", "发现", "home"), ("dynamic", "推荐动态", "news"), ("following", "关注动态", "person"), ("games", "Steam 游戏", "games"), ("favorites", "云端收藏", "bookmark"), ("history", "本机历史", "history") })
        {
            var b = Button("", () => Run(() => Navigate(page)), "nav"); b.HorizontalAlignment = HorizontalAlignment.Stretch; b.Content = Row(10, Icons.Get(icon, 18), Text(T(label))); if (_vm.Page == page || page == "games" && _vm.Page == "news") b.Classes.Add("selected"); nav.Children.Add(b);
        }
        var bottom = Stack(4, Button(T(_vm.AccountName), () => Run(() => Navigate("account")), "nav"), Button(T("设置"), () => Run(() => Navigate("settings")), "nav")); bottom.Margin = new(12, 12);
        var content = new Grid { RowDefinitions = new("Auto,Auto,*,Auto") }; content.Children.Add(brand); Grid.SetRow(nav, 1); content.Children.Add(nav); Grid.SetRow(bottom, 3); content.Children.Add(bottom);
        _sidebar.Content = Brush(Brush(new Border { Child = content, BorderThickness = new Thickness(0, 0, 1, 0) }, Border.BackgroundProperty, "Sidebar"), Border.BorderBrushProperty, "Line");
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
            _title.Text = T(_vm.Title); RenderSidebar();
            if (_vm.Page == "settings") { _body.Content = BuildSettings(); return; }
            if (_vm.Page == "account") { _body.Content = BuildAccount(); return; }
            if (_vm.Page == "games") { _body.Content = BuildGames(); return; }
            Detach(_listHeader); Detach(_list); Detach(_listFooter);
            _readingGrid.Children.Clear(); _readingGrid.ColumnDefinitions = new($"{Math.Clamp(_vm.Prefs.ListWidth, 280, 480)},5,*");
            var listPanel = new Grid { RowDefinitions = new("Auto,*,Auto") }; listPanel.Children.Add(_listHeader); Grid.SetRow(_list, 1); listPanel.Children.Add(_list); Grid.SetRow(_listFooter, 2); listPanel.Children.Add(_listFooter);
            var listBorder = Brush(new Border { Child = listPanel, BorderThickness = new Thickness(0, 0, 1, 0) }, Border.BorderBrushProperty, "Line"); _readingGrid.Children.Add(listBorder);
            var splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent }; Grid.SetColumn(splitter, 1); _readingGrid.Children.Add(splitter);
            splitter.DragCompleted += (_, _) => { _vm.Prefs.ListWidth = Math.Clamp(_readingGrid.ColumnDefinitions[0].ActualWidth, 280, 480); _vm.Store.SavePreferences(); };
            Grid.SetColumn(_detail, 2); _readingGrid.Children.Add(_detail); _body.Content = _readingGrid;
            _listHeader.Content = BuildListHeader(); _listFooter.Content = BuildListFooter(); RenderReader(); UpdateResponsive();
            _status.Text = _vm.Loading ? T("正在加载…") : _vm.Demo ? "离线演示内容 · 不代表在线资讯" : $"{_vm.Stories.Count} 篇内容" + (_vm.BlockedCount > 0 ? $" · 已屏蔽 {_vm.BlockedCount} 篇" : "");
        }
        finally { _rendering = false; }
    }
    private Control BuildListHeader()
    {
        var children = Stack(10); children.Margin = new(16, 14, 16, 12);
        if (_vm.Page == "discover")
        {
            var tabs = Row(4);
            foreach (var (value, name) in new[] { ("all", "全部"), ("news", "资讯"), ("community", "社区") }) { var b = AsyncButton(T(name), async () => { _vm.Channel = value; await _vm.LoadAsync(); }, value == _vm.Channel ? "primary" : "flat"); tabs.Children.Add(b); }
            children.Children.Add(tabs);
        }
        if (_vm.Page == "following")
        {
            var authors = new ComboBox { ItemsSource = _vm.Following, SelectedItem = _vm.FollowingFilter ?? _vm.Following.FirstOrDefault(), HorizontalAlignment = HorizontalAlignment.Stretch, ItemTemplate = new FuncDataTemplate<Author>((a, _) => Text(a?.Name ?? "")) };
            authors.SelectionChanged += (_, _) => { var chosen = authors.SelectedItem as Author; if (chosen != _vm.FollowingFilter) { _vm.FollowingFilter = chosen; _vm.ApplyFilter(); } }; children.Children.Add(authors);
        }
        if (_vm.Page == "favorites") { var combo = new ComboBox { ItemsSource = _vm.Folders, SelectedItem = _vm.Folder ?? _vm.Folders.FirstOrDefault(), HorizontalAlignment = HorizontalAlignment.Stretch }; combo.SelectionChanged += async (_, _) => { if (combo.SelectedItem is Folder f && f != _vm.Folder) { _vm.Folder = f; await _vm.LoadAsync(); } }; children.Children.Add(combo); }
        if (_vm.Page == "search")
        {
            children.Children.Add(Text(_vm.SearchScope == null ? $"“{_vm.Query}”" : $"{_vm.SearchScope.Name} · {_vm.Query}", 14, true));
            if (_vm.SearchScope?.Kind == "topic") children.Children.Add(Text("在话题列表中分批匹配标题与摘要", 11, muted: true));
        }
        if (_vm.Page == "destination" && _vm.Target is { } target)
        {
            children.Children.Add(Text(_vm.AuthorPage?.Author.Name is { Length: > 0 } name ? name : target.Name, 18, true));
            if (_vm.AuthorPage is { } author) { children.Children.Add(Text(author.Bio, 12, muted: true)); children.Children.Add(Text($"{author.Following?.ToString() ?? "—"} 关注 · {author.Followers?.ToString() ?? "—"} 粉丝 · {author.Likes?.ToString() ?? "—"} 获赞", 11, muted: true)); }
            if (target.Kind == "author") children.Children.Add(Row(6, AsyncButton("文章", async () => { _vm.AuthorMoments = false; await _vm.LoadAsync(); }, !_vm.AuthorMoments ? "primary" : "flat"), AsyncButton("动态", async () => { _vm.AuthorMoments = true; await _vm.LoadAsync(); }, _vm.AuthorMoments ? "primary" : "flat")));
            children.Children.Add(Row(6, AsyncButton(T("在此搜索"), async () => { _vm.SearchScope = target.Kind == "author" ? null : target; await _vm.NavigateAsync("search"); }), AsyncButton(T("屏蔽"), async () => { var kind = target.Kind switch { "author" => RuleKind.AUTHOR, "section" => RuleKind.SECTION, _ => RuleKind.TOPIC }; AddRule(new(kind, target.Id, target.Name, Source: Source.HEYBOX)); await Task.CompletedTask; })));
        }
        if (_vm.BlockedCount > 0 || _vm.Reveal) children.Children.Add(Button(T("显示已屏蔽内容"), () => _vm.Reveal = !_vm.Reveal, "flat"));
        return children;
    }
    private Control BuildListFooter()
    {
        var stack = Stack(8); stack.Margin = new(16, 12);
        if (_vm.Error.Length > 0)
        {
            stack.Children.Add(Text(_vm.Error, 12)); stack.Children.Add(AsyncButton(T("重试"), () => _vm.LoadAsync(_vm.Stories.Count > 0)));
            if (_vm.Error.Contains("验证")) stack.Children.Add(Button(T("官方验证入口"), Verify));
            if (_vm.Api.Session == null && _vm.Page is "favorites" or "following" or "cloudhistory") stack.Children.Add(AsyncButton(T("登录小黑盒"), LoginDialog));
        }
        else if (_vm.Loading) stack.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 3 });
        else if (_vm.HasMore) stack.Children.Add(AsyncButton(T("加载更多"), () => _vm.LoadAsync(true)));
        else if (_vm.Stories.Count == 0) { stack.Children.Add(Text(T("暂无内容"), 14, muted: true)); if (_vm.Page == "search") stack.Children.Add(SearchHistory()); }
        return stack;
    }
    private Control StoryRow(Story? story)
    {
        if (story == null) return new Border();
        var compact = _vm.Prefs.CardLayout == "Compact"; var cover = !_vm.Prefs.NoImages && story.Image != null;
        var title = Text(story.Title, 14, true); ToolTip.SetTip(title, story.Title); title.MaxLines = compact ? 2 : 3; title.TextTrimming = TextTrimming.CharacterEllipsis;
        var meta = Text(story.Metadata, 11, muted: true); meta.MaxLines = 1;
        var details = Stack(8, title, meta); details.VerticalAlignment = VerticalAlignment.Center;
        Control content = details;
        if (_vm.Prefs.CardLayout == "Gallery" && cover) content = Stack(12, new RemoteImage(_images, story.Image, 150), details);
        else if (cover) content = Columns("*,12,96", details, new Border(), new RemoteImage(_images, story.Image, 64));
        if (_vm.Prefs.CardLayout == "Comfortable") content = Stack(10, content, Text(story.Excerpt, 12, muted: true));
        var border = Brush(new Border { Child = content, Padding = new(18, 18), MinHeight = compact ? 102 : 132, BorderThickness = new(0, 0, 0, 1) }, Border.BorderBrushProperty, "Line");
        var menu = new ContextMenu();
        foreach (var (label, action) in new (string, Action)[] { ("打开原文", () => OpenUrl(story.Url)), ("复制链接", () => Run(() => Clipboard?.SetTextAsync(story.Url ?? "") ?? Task.CompletedTask)), ("屏蔽关键词", () => Run(() => RuleDialog(new(RuleKind.KEYWORD, "")))), ("屏蔽作者", () => { if (story.Profile?.Id is { Length: > 0 } id) AddRule(new(RuleKind.AUTHOR, id, story.Author, Source: Source.HEYBOX)); }) }) { var item = new MenuItem { Header = T(label) }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        border.ContextMenu = menu; return border;
    }
    private void RenderReader()
    {
        if (_vm.Article is not { } article) { _detail.Content = EmptyReader(); return; }
        var story = article.Story;
        if (_renderedKey != story.Key)
        {
            var history = _vm.Store.History.FirstOrDefault(x => x.Story.Key == story.Key); _articleOffset = history?.ArticleOffset ?? 0; _commentOffset = history?.CommentOffset ?? 0; _renderedKey = story.Key;
        }
        Detach(_readerScroll);
        var frame = new Grid { RowDefinitions = new("42,*,48") };
        var tabs = Row(4, Button(T("正文"), () => SwitchComments(false), !_vm.CommentsMode ? "primary" : "flat")); tabs.Margin = new(20, 6);
        if (story.Source == Source.HEYBOX) tabs.Children.Add(Button(T("评论") + $" {_vm.Comments.Count}", () => SwitchComments(true), _vm.CommentsMode ? "primary" : "flat"));
        if (_vm.DetailLoading) tabs.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 70, Height = 3 });
        frame.Children.Add(tabs);
        var content = _vm.CommentsMode ? BuildComments() : BuildArticle(article); _readerScroll.Content = content; _readerScroll.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        Grid.SetRow(_readerScroll, 1); frame.Children.Add(_readerScroll);
        var actions = Row(8);
        if (story.Source == Source.HEYBOX)
        {
            actions.Children.Add(AsyncButton(T(article.Liked == true ? "已点赞" : "点赞"), () => Social("like"), "flat"));
            actions.Children.Add(AsyncButton(T(article.Favorited == true ? "已收藏" : "收藏"), () => Social("favorite"), "flat"));
            actions.Children.Add(AsyncButton(T("写评论"), () => CommentDialog(null), "flat"));
        }
        actions.Children.Add(Button(T("打开原文"), () => OpenUrl(story.Url), "flat")); actions.Children.Add(Button(T("分享"), () => Native.Share(story.Url ?? story.Title), "flat"));
        var footer = Brush(new Border { Child = actions, Padding = new(20, 7), BorderThickness = new(0, 1, 0, 0) }, Border.BorderBrushProperty, "Line"); Grid.SetRow(footer, 2); frame.Children.Add(footer); _detail.Content = frame;
        var offset = _vm.CommentsMode ? _commentOffset : _articleOffset; Dispatcher.UIThread.Post(() => { if (_renderedKey == story.Key) _readerScroll.Offset = new(0, offset); }, DispatcherPriority.Loaded);
    }
    private Control BuildArticle(Article article)
    {
        var story = article.Story; var stack = Stack(0); stack.MaxWidth = 760; stack.Margin = new Thickness(36, 22, 36, 36); stack.HorizontalAlignment = HorizontalAlignment.Stretch;
        stack.Children.Add(Text(story.Title, 26, true));
        var authorButton = Button(story.Author, () => { if (story.Profile?.Id is { Length: > 0 } id) Run(() => _vm.NavigateAsync("destination", new("author", id, story.Author))); }, "flat"); authorButton.Padding = new(0);
        var authorRow = Row(8, authorButton, Text(story.Date, 11, muted: true)); authorRow.Margin = new(0, 12, 0, 18);
        if (story.Source == Source.HEYBOX && story.Profile?.Id is { Length: > 0 }) authorRow.Children.Add(AsyncButton(T(article.Followed == true ? "已关注" : "关注"), () => Social("follow"), "flat")); stack.Children.Add(authorRow);
        var labels = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var label in story.Sections ?? []) labels.Children.Add(Button(label.Name, () => Run(() => _vm.NavigateAsync("destination", new("section", label.Id, label.Name))), "flat"));
        foreach (var label in story.Topics ?? []) labels.Children.Add(Button("#" + label.Name, () => Run(() => _vm.NavigateAsync("destination", new("topic", label.Id, label.Name))), "flat"));
        if (labels.Children.Count > 0) { labels.Margin = new(0, 0, 0, 14); stack.Children.Add(labels); }
        if (_vm.DetailError.Length > 0) stack.Children.Add(Group(Text(_vm.DetailError), AsyncButton(T("重试"), _vm.RetryDetailAsync), Button(T("官方验证入口"), Verify)));
        if (story.Notice != null) { var notice = Text(story.Notice, 12, muted: true); notice.Margin = new(0, 0, 0, 18); stack.Children.Add(notice); }
        var blocks = story.Blocks ?? ContentParser.Html(story.Body.Length > 0 ? story.Body : story.Excerpt);
        if (!_vm.Prefs.NoImages && story.Image != null && !blocks.Any(x => x.Kind == BlockKind.Image && x.Url == story.Image)) { var image = new RemoteImage(_images, story.Image, 220); image.Margin = new(0, 0, 0, 22); image.Tapped += (_, _) => Run(() => ImageViewer([story.Image], 0)); stack.Children.Add(image); }
        if (story.Source == Source.STEAM)
        {
            stack.Children.Add(AsyncButton(_vm.Translating ? "正在准备翻译资源…" : T("翻译为中文"), _vm.TranslateAsync));
            if (_vm.Translated.Length > 0) { var translated = ReadingText(_vm.Translated); translated.Margin = new(0, 16, 0, 22); stack.Children.Add(Group(Text(T("译文"), 14, true), translated)); stack.Children.Add(Text(T("原文"), 16, true)); }
        }
        var gallery = blocks.Where(b => b.Kind == BlockKind.Image && b.Url != null).Select(b => b.Url!).ToArray();
        foreach (var block in blocks) stack.Children.Add(RenderBlock(block, gallery));
        return stack;
    }
    private Control RenderBlock(ArticleBlock block, string[] gallery)
    {
        Control control;
        switch (block.Kind)
        {
            case BlockKind.Image:
                if (_vm.Prefs.NoImages) control = Button("图片 · 点击查看", () => Run(() => ImageViewer(gallery, Array.IndexOf(gallery, block.Url!))), "flat");
                else { var image = new RemoteImage(_images, block.Url, 280); image.Tapped += (_, _) => Run(() => ImageViewer(gallery, Array.IndexOf(gallery, block.Url!))); control = Stack(6, image, Text(block.Caption ?? "", 11, muted: true)); }
                break;
            case BlockKind.Heading: control = Text(block.Text, block.Level <= 2 ? 21 : 18, true); break;
            case BlockKind.Quote: var children = Stack(8); foreach (var child in block.Children ?? []) children.Children.Add(RenderBlock(child, gallery)); control = Brush(new Border { Child = children, BorderThickness = new(3, 0, 0, 0), Padding = new(16, 6), Background = Brushes.Transparent }, Border.BorderBrushProperty, "Accent"); break;
            case BlockKind.Game: control = Group(Text(block.Text.Length > 0 ? block.Text : $"Steam App {block.AppId}", 15, true), AsyncButton(T("查看游戏公告"), async () => { if (block.AppId is { } app) await _vm.NavigateAsync("news", game: new(app, block.Text, null)); else OpenUrl(block.Url); })); break;
            case BlockKind.Video: control = block.Url == null ? Text("视频暂不可用") : new NativeVideo(block.Url) { Height = 300 }; break;
            case BlockKind.ExternalVideo: control = Button(block.Text, () => OpenUrl(block.Url), "flat"); break;
            default: control = RichText(block); break;
        }
        control.Margin = new(0, 0, 0, _vm.Prefs.ParagraphSpacing); return control;
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
        var runs = block.Runs ?? [new RichRun((block.Kind == BlockKind.ListItem ? "•  " : "") + block.Text)];
        foreach (var run in runs)
        {
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
                var link = Button(value, () => OpenUrl(run.Url), "flat"); link.Padding = new(0); link.FontSize = _vm.Prefs.BodySize; ToolTip.SetTip(link, run.Url); text.Inlines!.Add(new InlineUIContainer(link)); return;
            }
            text.Inlines!.Add(new Run(value) { FontWeight = run.Bold ? FontWeight.Bold : FontWeight.Normal, FontStyle = run.Italic ? FontStyle.Italic : FontStyle.Normal, FontFamily = run.Code ? new("Menlo, Monaco") : text.FontFamily });
        }
    }
    private void ConfigureMenus()
    {
        var root = new NativeMenu();
        var navigation = new NativeMenu();
        foreach (var (label, page) in new[] { ("发现", "discover"), ("推荐动态", "dynamic"), ("关注动态", "following"), ("Steam 游戏", "games"), ("云端收藏", "favorites"), ("本机历史", "history"), ("设置", "settings") }) { var item = new NativeMenuItem { Header = T(label) }; item.Click += (_, _) => Run(() => Navigate(page)); navigation.Items.Add(item); }
        root.Items.Add(new NativeMenuItem { Header = "浏览", Menu = navigation });
        var reading = new NativeMenu(); var original = new NativeMenuItem { Header = T("打开原文") }; original.Click += (_, _) => OpenUrl(_vm.Article?.Story.Url); reading.Items.Add(original); var translate = new NativeMenuItem { Header = T("翻译为中文") }; translate.Click += (_, _) => Run(_vm.TranslateAsync); reading.Items.Add(translate); root.Items.Add(new NativeMenuItem { Header = "阅读", Menu = reading });
        NativeMenu.SetMenu(this, root);
    }
    private FontFamily ResolveFont(string name)
    {
        if (name == "System") return new("PingFang SC, Helvetica Neue, Arial"); if (name == "Serif") return new("Songti SC, Georgia"); if (name == "Mono") return new("Menlo, Monaco"); if (name == "Sans") return new("PingFang SC, Helvetica Neue");
        var path = Path.Combine(_vm.Store.Root, "fonts", Path.GetFileName(name)); if (File.Exists(path)) return new(Native.RegisterFont(path) ?? "PingFang SC"); return new(name);
    }
    private void SwitchComments(bool value) { if (_vm.CommentsMode == value) return; SaveOffsets(); _vm.CommentsMode = value; Render(); }
    private Control BuildComments()
    {
        var stack = Stack(18); stack.Margin = new(30, 22); stack.Children.Add(Text(T("评论"), 22, true));
        if (_vm.Comments.Count == 0) stack.Children.Add(Text(_vm.Api.Session == null ? "登录后查看完整评论" : T("暂无内容"), 13, muted: true));
        foreach (var c in _vm.Comments) stack.Children.Add(CommentView(c));
        if (_vm.MoreComments) stack.Children.Add(AsyncButton(T("加载更多"), _vm.MoreCommentsAsync)); return stack;
    }
    private Control CommentView(Comment c, bool reply = false)
    {
        var name = Button(c.Author.Name, () => { if (c.Author.Id.Length > 0) Run(() => _vm.NavigateAsync("destination", new("author", c.Author.Id, c.Author.Name))); }, "flat"); name.Padding = new(0);
        var body = Stack(8, Row(8, name, Text(c.Date, 11, muted: true)), ReadingText(c.Text));
        if (!_vm.Prefs.NoImages) foreach (var url in c.Images) { var image = new RemoteImage(_images, url, 150); image.Tapped += (_, _) => Run(() => ImageViewer(c.Images.ToArray(), Array.IndexOf(c.Images.ToArray(), url))); body.Children.Add(image); }
        body.Children.Add(Row(8, AsyncButton(T("回复"), () => CommentDialog(c), "flat"), AsyncButton($"↑ {c.Supports}", () => Social("support", c), "flat")));
        if (!reply) { foreach (var child in c.Replies) body.Children.Add(CommentView(child, true)); body.Children.Add(AsyncButton(T("查看楼中楼"), async () => { await RepliesDialog(c); }, "flat")); }
        var border = Brush(new Border { Child = body, Padding = new(reply ? 16 : 0, 0, 0, 18), BorderThickness = new(0, 0, 0, 1) }, Border.BorderBrushProperty, "Line"); return border;
    }
    private Control EmptyReader() => new Border { Child = Stack(12, Icons.Get("book_open", 40), Text(T("选择一篇文章开始阅读"), 16, muted: true)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private Control SearchHistory()
    {
        var s = Stack(8, Text(T("搜索记录"), 14, true)); foreach (var h in _vm.Store.Searches.Take(12)) s.Children.Add(AsyncButton(h.Query + (h.Scope == null ? "" : $" · {h.Scope.Name}"), async () => { _vm.Query = h.Query; _search.Text = h.Query; _vm.SearchScope = h.Scope; await _vm.SearchAsync(); }, "flat"));
        s.Children.Add(Button(T("清除记录"), () => { _vm.Store.ClearSearches(); Render(); }, "flat")); return s;
    }
    private Control BuildGames()
    {
        var s = Stack(18, Text(T("Steam 游戏"), 24, true), Text("输入游戏名称或 AppID，浏览 Steam 社区公告。", 13, muted: true)); s.Margin = new(32);
        if (_vm.Loading) s.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 3 }); if (_vm.Error.Length > 0) s.Children.Add(Group(Text(_vm.Error), AsyncButton(T("重试"), () => _vm.LoadAsync())));
        foreach (var game in _vm.Games) { var b = AsyncButton("", async () => { _showDetail = false; await _vm.NavigateAsync("news", game: game); }); b.HorizontalContentAlignment = HorizontalAlignment.Stretch; b.Content = Row(16, new RemoteImage(_images, game.Image, 64) { Width = 140 }, Stack(6, Text(game.Name, 16, true), Text($"AppID {game.AppId}", 12, muted: true))); s.Children.Add(b); }
        if (_vm.Games.Count == 0 && !_vm.Loading) s.Children.Add(SearchHistory()); return new ScrollViewer { Content = s };
    }
    private Control BuildAccount()
    {
        var s = Stack(24, Text(T("账号概览"), 25, true), Group(Text(_vm.AccountName, 18, true), Text(_vm.Api.Session == null ? "扫码登录以同步小黑盒账号内容" : $"Heybox ID · {_vm.Api.Session.UserId}", 13, muted: true))); s.Margin = new(36); s.MaxWidth = 740;
        if (_vm.Api.Session == null) s.Children.Add(AsyncButton(T("登录小黑盒"), LoginDialog, "primary"));
        else { s.Children.Add(Group(AsyncButton(T("云端收藏"), () => Navigate("favorites")), AsyncButton(T("账号历史"), () => Navigate("cloudhistory")), AsyncButton(T("关注动态"), () => Navigate("following")))); s.Children.Add(AsyncButton(T("退出登录"), async () => { if (await Confirm(T("退出登录"), "退出当前账号？本机历史与阅读偏好会保留。")) { _vm.Logout(); Render(); } })); }
        return new ScrollViewer { Content = s };
    }
    private void ApplyTheme()
    {
        if (Application.Current is not { } app) return;
        app.RequestedThemeVariant = _vm.Prefs.Theme switch { "Dark" => ThemeVariant.Dark, "Light" => ThemeVariant.Light, _ => ThemeVariant.Default };
        app.Resources["Accent"] = Avalonia.Media.Brush.Parse(_vm.Prefs.Accent switch { "Yellow" => "#AD6900", "Green" => "#218455", "Purple" => "#8355CB", "Blue" => "#2563D9", _ => Native.Accent() });
        FontFamily = ResolveFont(_vm.Prefs.UiFont);
        _body.Transitions = _vm.Prefs.Animations && (!OperatingSystem.IsMacOS() || Native.hr_reduce_motion() == 0) ? new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(120), Easing = new CubicEaseOut() } } : null;
    }
    private void UpdateResponsive()
    {
        _narrow = Bounds.Width < 1120;
        if (_vm.Page is "settings" or "account" or "games") return;
        if (_readingGrid.Children.Count < 3) return;
        var list = _readingGrid.Children[0]; var splitter = _readingGrid.Children[1];
        list.IsVisible = !_narrow || !_showDetail; splitter.IsVisible = !_narrow; _detail.IsVisible = !_narrow || _showDetail;
        _readingGrid.ColumnDefinitions[0].Width = _narrow ? _showDetail ? new(0) : new(1, GridUnitType.Star) : new(Math.Clamp(_vm.Prefs.ListWidth, 280, 480));
        _readingGrid.ColumnDefinitions[1].Width = new(_narrow ? 0 : 5); _readingGrid.ColumnDefinitions[2].Width = _narrow && !_showDetail ? new(0) : new(1, GridUnitType.Star);
    }
    private static void Detach(Control control) { if (control.Parent is Panel p) p.Children.Remove(control); else if (control.Parent is ContentControl c) c.Content = null; else if (control.Parent is Border b) b.Child = null; }
    private void SaveOffsets() { if (_vm.Article is { } article && _renderedKey == article.Story.Key) _vm.Store.Visit(article.Story, _articleOffset, _commentOffset); }
    private async Task Social(string action, Comment? comment = null) { try { await _vm.SocialAsync(action, comment); } catch (Exception e) { await Notice(e.Message); } }
    private async void Run(Func<Task> action) { try { await action(); } catch (OperationCanceledException) { } catch (Exception e) { Console.Error.WriteLine(e); _status.Text = e.Message; await Notice(e.Message); } }
    private void OpenUrl(string? url) { if (J.Https(url) != null) Run(async () => { await Launcher.LaunchUriAsync(new Uri(url!)); }); }
    private void Verify() => Native.Verify(_vm.Article?.Story.Url ?? "https://www.xiaoheihe.cn/");
    private void OnKeys(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) { switch (e.Key) { case Key.F: _search.Focus(); e.Handled = true; break; case Key.R: Run(() => _vm.LoadAsync()); e.Handled = true; break; case Key.OemComma: Run(() => Navigate("settings")); e.Handled = true; break; case Key.Left: Run(() => _vm.BackAsync()); e.Handled = true; break; case Key.Right: Run(() => _vm.BackAsync(true)); e.Handled = true; break; } }
        if (e.Key == Key.Escape && _narrow && _showDetail) { _showDetail = false; UpdateResponsive(); e.Handled = true; }
    }
}
