using HeyRead.Core;
namespace HeyRead.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private sealed record CachedPage(Story[] Stories, int Offset, string? Cursor, bool HasMore, DateTimeOffset Saved, AuthorPage? Author);
    private readonly Dictionary<string, CachedPage> _pageCache = [];
    private readonly HashSet<string> _socialOperations = [];
    private string? _restoreFollowingId;
    public NavigationState CurrentViewport { get; private set; } = new();
    public NavigationState CaptureNavigation() => new(Page, Target, Game, Query, SearchScope, Channel, AuthorMoments, Folder, FollowingFilter?.Id, Article?.Story is { } story ? LibraryStore.Summary(story) : null, CurrentViewport.ListOffset, CurrentViewport.ArticleOffset, CurrentViewport.CommentOffset, CurrentViewport.CommentsMode, CurrentViewport.ShowDetail);
    private NavigationState CommittedNavigation() => CaptureNavigation() with { Query = CurrentViewport.Query, SearchScope = CurrentViewport.SearchScope };
    public void SaveViewport(double list, double article, double comments, bool detail)
    {
        CurrentViewport = CommittedNavigation() with { ListOffset = list, ArticleOffset = article, CommentOffset = comments, CommentsMode = false, ShowDetail = detail };
        if (Article is { } current) Store.Visit(current.Story, article, comments);
        SaveNavigation();
    }
    public void SaveNavigation()
    {
        if (Prefs.RestoreNavigation) Prefs.Navigation = CommittedNavigation().Normalize();
        else { Prefs.Navigation = null; Prefs.LastStory = null; }
        Store.SavePreferences();
    }
    private void RememberCurrent()
    {
        if (Page is "account" or "settings" or "history" || _raw.Count == 0) return;
        _pageCache[CommittedNavigation().CacheKey] = new(_raw.Take(600).Select(LibraryStore.Summary).ToArray(), _offset, _cursor, HasMore, DateTimeOffset.UtcNow, AuthorPage);
        while (_pageCache.Count > 6) _pageCache.Remove(_pageCache.MinBy(x => x.Value.Saved).Key);
    }
    private static void PushBounded(Stack<NavigationState> stack, NavigationState value)
    {
        if (stack.Count >= 64) { var keep = stack.Take(63).Reverse().ToArray(); stack.Clear(); foreach (var item in keep) stack.Push(item); }
        stack.Push(value);
    }
    private static Comment UpdateComment(Comment item, Comment updated) => item.Id == updated.Id ? updated : item with { Replies = item.Replies.Select(c => UpdateComment(c, updated)).ToArray() };
    public void Suspend()
    {
        ++_navigationVersion; RememberCurrent(); Cancel(ref _feed); Cancel(ref _detail); Cancel(ref _translate);
        Article = null; Comments.Clear(); Loading = false; DetailLoading = false; Translating = false; ClearTranslation();
    }
    private void ResetAccountState()
    {
        Cancel(ref _feed); Cancel(ref _detail); Cancel(ref _translate);
        _pageCache.Clear(); _back.Clear(); _forward.Clear(); _socialOperations.Clear(); _moreComments = false; CommentsLoading = false; CommentsError = "";
        Loading = false; DetailLoading = false; Translating = false; ClearTranslation(); FollowingFilter = null; Folder = null;
    }
}
