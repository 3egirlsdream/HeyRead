using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HeyRead.Core;
namespace HeyRead.Desktop.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    public HeyReadApi Api { get; }
    public SteamService Steam { get; }
    public LibraryStore Store { get; }
    public ISessionStore Sessions { get; }
    private readonly ITranslationService _translation;
    public Preferences Prefs => Store.Preferences;
    public ObservableCollection<Story> Stories { get; } = [];
    public ObservableCollection<Game> Games { get; } = [];
    public ObservableCollection<Author> Following { get; } = [];
    public Author? FollowingFilter { get; set; }
    public AuthorPage? AuthorPage { get; private set; }
    public ObservableCollection<Folder> Folders { get; } = [];
    public ObservableCollection<Comment> Comments { get; } = [];
    public bool Demo { get; } = Environment.GetEnvironmentVariable("HEYREAD_DEMO") == "1";
    private string _page = "discover"; public string Page { get => _page; private set => SetProperty(ref _page, value); }
    private string _title = "发现"; public string Title { get => _title; private set => SetProperty(ref _title, value); }
    private string _error = ""; public string Error { get => _error; private set => SetProperty(ref _error, value); }
    private bool _loading; public bool Loading { get => _loading; private set => SetProperty(ref _loading, value); }
    private bool _detailLoading; public bool DetailLoading { get => _detailLoading; private set => SetProperty(ref _detailLoading, value); }
    private Article? _article; public Article? Article { get => _article; private set => SetProperty(ref _article, value); }
    private string _detailError = ""; public string DetailError { get => _detailError; private set => SetProperty(ref _detailError, value); }
    private string _toast = ""; public string Toast { get => _toast; set => SetProperty(ref _toast, value); }
    private string _query = ""; public string Query { get => _query; set => SetProperty(ref _query, value); }
    private bool _hasMore; public bool HasMore { get => _hasMore; private set => SetProperty(ref _hasMore, value); }
    private bool _commentsMode; public bool CommentsMode { get => _commentsMode; set => SetProperty(ref _commentsMode, value); }
    private bool _reveal; public bool Reveal { get => _reveal; set { if (SetProperty(ref _reveal, value)) ApplyFilter(); } }
    private string _translated = ""; public string Translated { get => _translated; private set => SetProperty(ref _translated, value); }
    private bool _translating; public bool Translating { get => _translating; private set => SetProperty(ref _translating, value); }
    public string Channel { get; set; } = "all";
    public bool AuthorMoments { get; set; }
    public Destination? Target { get; private set; }
    public Destination? SearchScope { get; set; }
    public Game? Game { get; private set; }
    public Folder? Folder { get; set; }
    public string AccountName => Api.Session?.Nickname is { Length: > 0 } name ? name : Api.Session?.UserId ?? "登录小黑盒";
    public int BlockedCount => _raw.Count - Stories.Count;
    public event Action? ContentChanged;
    public event Action? SettingsChanged;
    private readonly List<Story> _raw = [];
    private readonly Stack<(string Page, Destination? Target, Game? Game, string Query)> _back = [];
    private readonly Stack<(string Page, Destination? Target, Game? Game, string Query)> _forward = [];
    private CancellationTokenSource _feed = new(), _detail = new(), _translate = new();
    private int _offset, _commentPage = 1; private string? _cursor; private bool _moreComments;
    public MainViewModel(HeyReadApi api, SteamService steam, LibraryStore store, ISessionStore sessions, ITranslationService translation) { Api = api; Steam = steam; Store = store; Sessions = sessions; _translation = translation; }
    public async Task InitializeAsync()
    {
        var page = Prefs.RestoreNavigation ? Prefs.LastPage : "discover";
        if (page is not ("discover" or "dynamic" or "following" or "games" or "favorites" or "history" or "cloudhistory" or "account" or "settings")) page = "discover";
        await NavigateAsync(page, remember: false);
        if (Prefs.RestoreNavigation && Prefs.LastStory is { } story && !Demo) await SelectAsync(story);
    }
    public async Task NavigateAsync(string page, Destination? target = null, Game? game = null, bool remember = true)
    {
        if (remember) { _back.Push((Page, Target, Game, Query)); _forward.Clear(); }
        Cancel(ref _feed); Cancel(ref _detail); Cancel(ref _translate);
        DetailLoading = false; Translating = false; Article = null; DetailError = ""; Translated = ""; CommentsMode = false; Reveal = false;
        Page = page; Target = target; Game = game; AuthorPage = null; FollowingFilter = null;
        Title = page switch { "discover" => "发现", "dynamic" => "推荐动态", "following" => "关注动态", "games" => "Steam 游戏", "news" => game?.Name ?? "游戏公告", "favorites" => "云端收藏", "history" => "本机历史", "cloudhistory" => "账号历史", "search" => "搜索结果", "destination" => target?.Name ?? "浏览", "account" => "账号概览", "settings" => "设置", _ => "盒阅" };
        if (Demo) Title += " · 离线演示";
        Prefs.LastPage = page; Store.SavePreferences(); ContentChanged?.Invoke(); await LoadAsync();
    }
    public async Task BackAsync(bool forward = false)
    {
        var source = forward ? _forward : _back; var destination = forward ? _back : _forward; if (source.Count == 0) return;
        destination.Push((Page, Target, Game, Query)); var state = source.Pop(); Query = state.Query; await NavigateAsync(state.Page, state.Target, state.Game, false);
    }
    public async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(Query)) { await NavigateAsync("search"); return; }
        Store.Search(Query.Trim(), SearchScope); await NavigateAsync(Page is "games" or "news" ? "games" : "search");
    }
    public async Task LoadAsync(bool more = false)
    {
        if (Loading && more) return;
        if (!more) { Cancel(ref _feed); _raw.Clear(); Stories.Clear(); Games.Clear(); _offset = 0; _cursor = null; HasMore = false; }
        var token = _feed.Token; var pageName = Page; Error = ""; Loading = true;
        try
        {
            if (Page is "settings" or "account") return;
            if (Page == "history") { _raw.AddRange(Store.History.Select(x => x.Story)); HasMore = false; ApplyFilter(); return; }
            if (Demo && Page is "discover" or "dynamic" or "destination" or "search") { _raw.AddRange(DemoStories.Items); HasMore = false; ApplyFilter(); if (Article == null) await SelectAsync(Stories.FirstOrDefault()); return; }
            if (Page == "search" && string.IsNullOrWhiteSpace(Query)) return;
            if (Page == "games") { if (!string.IsNullOrWhiteSpace(Query)) foreach (var g in await Steam.SearchAsync(Query, token)) Games.Add(g); return; }
            Page<Story> page;
            if (Page == "news" && Game != null) page = await Steam.NewsAsync(Game, _cursor, token);
            else if (Page == "destination" && Target != null)
            {
                if (!more && Target.Kind == "author") { try { AuthorPage = await Api.ProfileAsync(Target.Id, token); } catch (Exception e) when (e is not OperationCanceledException) { Toast = e.Message; } }
                page = await Api.DestinationAsync(Target, _offset, _cursor, AuthorMoments, token);
            }
            else if (Page == "search") page = await Api.SearchAsync(Query.Trim(), _offset, SearchScope, _cursor, token);
            else if (Page is "favorites" or "cloudhistory" or "following")
            {
                if (Page == "following" && !more) { Following.Clear(); Following.Add(new("", "全部关注")); foreach (var author in await Api.FollowingAsync(token)) Following.Add(author); }
                if (Page == "favorites" && !more) { Folders.Clear(); Folders.Add(new("", "全部收藏")); foreach (var f in await Api.FoldersAsync(token)) Folders.Add(f); }
                page = await Api.AccountListAsync(Page == "cloudhistory" ? "history" : Page, _offset, string.IsNullOrEmpty(Folder?.Id) ? null : Folder.Id, _cursor, token);
            }
            else page = await Api.FeedAsync(_offset, Page == "dynamic", Channel, token);
            token.ThrowIfCancellationRequested(); if (pageName != Page) return;
            var previousOffset = _offset; var previousCursor = _cursor;
            foreach (var story in page.Items) if (!_raw.Any(x => x.Key == story.Key)) _raw.Add(story);
            _offset = page.NextOffset; _cursor = page.Cursor;
            HasMore = page.HasMore && (_offset != previousOffset || _cursor != previousCursor); ApplyFilter();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!token.IsCancellationRequested) { Error = e.Message; Expired(e); } }
        finally { if (!token.IsCancellationRequested) { Loading = false; ContentChanged?.Invoke(); } }
    }
    public void ApplyFilter()
    {
        var desired = Store.Filter(FollowingFilter is { Id.Length: > 0 } selected ? _raw.Where(x => x.Profile?.Id == selected.Id) : _raw, Page, Reveal).ToArray();
        for (var i = 0; i < desired.Length; i++)
        {
            if (i < Stories.Count && Stories[i].Key == desired[i].Key) continue;
            var existing = -1; for (var j = i + 1; j < Stories.Count; j++) if (Stories[j].Key == desired[i].Key) { existing = j; break; }
            if (existing >= 0) Stories.Move(existing, i); else Stories.Insert(i, desired[i]);
        }
        while (Stories.Count > desired.Length) Stories.RemoveAt(Stories.Count - 1);
        OnPropertyChanged(nameof(BlockedCount)); ContentChanged?.Invoke();
    }
    public async Task SelectAsync(Story? story)
    {
        if (story == null || Article?.Story.Key == story.Key) return;
        Cancel(ref _detail); Cancel(ref _translate); var token = _detail.Token;
        Translated = ""; Translating = false; CommentsMode = false; DetailError = ""; Article = new(story, []); Comments.Clear(); DetailLoading = true; ContentChanged?.Invoke();
        try
        {
            var article = story.Source == Source.DEMO ? new Article(story, []) : await Api.ArticleAsync(story, token);
            token.ThrowIfCancellationRequested(); Article = article; foreach (var c in article.Comments) Comments.Add(c); _commentPage = 1; _moreComments = article.HasMoreComments;
            Store.Visit(article.Story); Prefs.LastStory = article.Story; Store.SavePreferences();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!token.IsCancellationRequested) { DetailError = e.Message; Expired(e); } }
        finally { if (!token.IsCancellationRequested) { DetailLoading = false; ContentChanged?.Invoke(); } }
    }
    public async Task RetryDetailAsync() { var story = Article?.Story; Article = null; await SelectAsync(story); }
    public async Task MoreCommentsAsync()
    {
        if (Article == null) return;
        try { var page = await Api.CommentsAsync(Article.Story, _commentPage + 1, ct: _detail.Token); foreach (var c in page.Items) if (!Comments.Any(x => x.Id == c.Id)) Comments.Add(c); _commentPage++; _moreComments = page.HasMore; ContentChanged?.Invoke(); }
        catch (OperationCanceledException) { }
        catch (Exception e) { Toast = e.Message; Expired(e); }
    }
    public bool MoreComments => _moreComments;
    public Task<Page<Comment>> RepliesAsync(Comment root, string? cursor = null) => Api.CommentsAsync(Article!.Story, 1, root.Id, cursor, _detail.Token);
    public async Task SocialAsync(string action, Comment? reply = null, string? text = null)
    {
        var snapshot = Article; if (snapshot == null) return;
        var enabled = action switch { "like" => snapshot.Liked != true, "favorite" => snapshot.Favorited != true, "follow" => snapshot.Followed != true, "support" => reply?.Supported != true, _ => true };
        try
        {
            await Api.SocialAsync(snapshot.Story, action, enabled, text, snapshot.AuthCode, reply, _detail.Token);
            if (Article?.Story.Key != snapshot.Story.Key) return;
            Article = action switch { "like" => snapshot with { Liked = enabled }, "favorite" => snapshot with { Favorited = enabled }, "follow" => snapshot with { Followed = enabled }, _ => snapshot };
            Toast = "操作成功"; ContentChanged?.Invoke();
        }
        catch (Exception e) when (e is not OperationCanceledException) { Toast = e.Message; Expired(e); throw; }
    }
    public async Task TranslateAsync()
    {
        var story = Article?.Story; if (story?.Source != Source.STEAM || Translating) return;
        Cancel(ref _translate); var token = _translate.Token; Translating = true; Translated = "";
        try { var strings = (story.Blocks ?? []).Where(x => x.Kind is BlockKind.Paragraph or BlockKind.Heading or BlockKind.ListItem).Select(x => x.Text).Where(x => x.Length > 0).ToArray(); if (strings.Length == 0) strings = [ContentParser.Text(story.Body)]; var result = await _translation.TranslateAsync(strings, Prefs.Language == "zh-TW" ? "zh-Hant" : "zh-Hans", token); if (Article?.Story.Key == story.Key) Translated = string.Join("\n\n", result); }
        catch (OperationCanceledException) { }
        catch (Exception e) { Toast = "翻译失败：" + e.Message; }
        finally { if (!token.IsCancellationRequested) Translating = false; ContentChanged?.Invoke(); }
    }
    public void SaveSettings() { Store.SavePreferences(); SettingsChanged?.Invoke(); ContentChanged?.Invoke(); }
    public void Login(Session session) { Sessions.Save(session); Api.SetSession(session); OnPropertyChanged(nameof(AccountName)); ContentChanged?.Invoke(); }
    public void Logout() { Cancel(ref _feed); Cancel(ref _detail); Cancel(ref _translate); Api.SetSession(null); Sessions.Save(null); Article = null; Comments.Clear(); _raw.Clear(); ApplyFilter(); OnPropertyChanged(nameof(AccountName)); }
    private void Expired(Exception e) { if (e is ApiException { Status: "relogin" }) { Api.SetSession(null); Sessions.Save(null); OnPropertyChanged(nameof(AccountName)); } }
    private static void Cancel(ref CancellationTokenSource source) { source.Cancel(); source.Dispose(); source = new(); }
    public void Dispose() { _feed.Cancel(); _detail.Cancel(); _translate.Cancel(); _feed.Dispose(); _detail.Dispose(); _translate.Dispose(); }
}
public static class DemoStories
{
    public static readonly Story[] Items = [
        new("demo-1", Source.DEMO, "从艾尔登法环到博德之门：探索开放世界的无限可能", "当游戏世界不再被边界定义，我们如何找到属于自己的冒险？", "", "游戏观察室", "离线演示", Image: "https://cdn.akamai.steamstatic.com/steam/apps/1245620/header.jpg", AppId: 1245620, Profile: new("", "游戏观察室"), Sections: [new("", "单机游戏")], Topics: [new("", "开放世界")], Blocks: [new(BlockKind.Paragraph, "当我们再次踏入交界地，远处的城堡、山谷间的道路和偶然响起的风声，依然会让人想起第一次出发时的好奇。"), new(BlockKind.Heading, "每一条路，都有新的故事"), new(BlockKind.Paragraph, "开放世界的魅力，藏在那些没有被标记的转角。优秀的设计让探索自然发生，让玩家用自己的节奏，拼出一段独一无二的旅程。"), new(BlockKind.Quote, Children: [new(BlockKind.Paragraph, "真正令人难忘的冒险，是你选择走向未知的那一刻。")]), new(BlockKind.Paragraph, "这是一篇明确标注的离线演示文章，用于检查排版和窗口布局，不代表在线资讯。")]),
        new("demo-2", Source.DEMO, "独立游戏周报：值得留意的新故事", "小小的工作室，也能创造广阔的世界。", Author: "独立游戏电台", Date: "离线演示", Image: "https://cdn.akamai.steamstatic.com/steam/apps/413150/header.jpg"),
        new("demo-3", Source.DEMO, "光影之间，重新发现游戏中的城市", "从街角到天际线，观察空间里的细节。", Author: "像素漫游者", Date: "离线演示", Image: "https://cdn.akamai.steamstatic.com/steam/apps/1091500/header.jpg"),
        new("demo-4", Source.DEMO, "一份留给周末的阅读清单", "收藏那些值得慢慢读完的内容。", Author: "盒阅编辑部", Date: "离线演示") ];
}
