namespace HeyRead.Core;

/// <summary>Only metadata and viewport state are persisted; sessions and full article bodies never enter navigation files.</summary>
public sealed record NavigationState(
    string Page = "discover", Destination? Target = null, Game? Game = null,
    string Query = "", Destination? SearchScope = null, string Channel = "all",
    bool AuthorMoments = false, Folder? Folder = null, string? FollowingAuthorId = null,
    Story? SelectedStory = null, double ListOffset = 0, double ArticleOffset = 0,
    double CommentOffset = 0, bool CommentsMode = false, bool ShowDetail = false)
{
    [System.Text.Json.Serialization.JsonIgnore] public string CacheKey => string.Join('|', Page, Target?.Kind, Target?.Id, Game?.AppId, Query, SearchScope?.Kind, SearchScope?.Id, Channel, AuthorMoments, Folder?.Id);
    public NavigationState Normalize()
    {
        var allowed = new[] { "discover", "dynamic", "following", "games", "news", "favorites", "history", "cloudhistory", "search", "destination", "account", "settings" };
        if (!allowed.Contains(Page) || Page == "news" && Game is not { AppId: > 0 } || Page == "destination" && (Target?.Id.Length is not > 0 || Target.Kind is not ("author" or "section" or "topic"))) return new();
        return this with { Query = (Query ?? "").Length > 120 ? Query![..120] : Query ?? "", SelectedStory = SelectedStory == null ? null : LibraryStore.Summary(SelectedStory), ListOffset = Offset(ListOffset), ArticleOffset = Offset(ArticleOffset), CommentOffset = Offset(CommentOffset) };
    }
    private static double Offset(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 10000000) : 0;
}
