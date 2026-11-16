using System.Text.Json;
using HeyRead.Core;
using HeyRead.Desktop.Platform;
namespace HeyRead.Desktop;

public static class Probe
{
    /// <summary>Read-only integration check; no account identifiers, content or credentials enter the report.</summary>
    public static async Task AccountAsync()
    {
        using var api = new HeyReadApi(); var session = new LocalSessionStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "HeyRead")).Load(); api.SetSession(session);
        var results = new List<object>(); Story? first = null;
        async Task Check(string name, Func<Task<object>> action)
        {
            try { results.Add(new { name, status = "passed", detail = await action() }); }
            catch (Exception e) { results.Add(new { name, status = "failed", category = e is ApiException a ? a.Status ?? "api" : e.GetType().Name }); }
        }
        if (session?.IsValid == true)
        {
            await Check("folders", async () => new { count = (await api.FoldersAsync(default)).Count });
            await Check("favorites", async () => { var p = await api.AccountListAsync("favorites", 0); return new { count = p.Items.Count, p.HasMore }; });
            await Check("history", async () => { var p = await api.AccountListAsync("history", 0); first = p.Items.FirstOrDefault(); return new { count = p.Items.Count, p.HasMore }; });
            await Check("following", async () => new { count = (await api.FollowingAsync(default)).Count });
            if (first != null) await Check("article-and-comments", async () => { var article = await api.ArticleAsync(first); return new { blocks = article.Story.Blocks?.Count, comments = article.Comments.Count, article.HasMoreComments, commentsAvailable = article.CommentsError == null, hasSocialState = article.Liked != null || article.Favorited != null || article.Followed != null }; });
        }
        Console.WriteLine(JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, sessionRestored = session?.IsValid == true, accountWrites = "not performed", results }, new JsonSerializerOptions { WriteIndented = true }));
    }
    public static async Task RunAsync()
    {
        using var api = new HeyReadApi(); var results = new List<object>(); Story? first = null;
        async Task Check(string name, Func<Task<object>> action)
        {
            try { results.Add(new { name, status = "passed", detail = await action() }); }
            catch (Exception e) { results.Add(new { name, status = "failed", error = e.Message }); }
        }
        await Check("heybox-public-feed", async () => { var p = await api.FeedAsync(0); first = p.Items.FirstOrDefault(); return new { count = p.Items.Count, p.HasMore }; });
        await Check("heybox-public-search", async () => { var p = await api.SearchAsync("游戏", 0); return new { count = p.Items.Count, p.HasMore }; });
        if (first != null) await Check("heybox-public-article", async () => { var a = await api.ArticleAsync(first); return new { blocks = a.Story.Blocks?.Count, a.Story.Notice }; });
        var steam = new SteamService(api);
        await Check("steam-game-search", async () => new { count = (await steam.SearchAsync("Dota", default)).Count });
        await Check("steam-game-appid", async () => new { count = (await steam.SearchAsync("570", default)).Count });
        await Check("steam-announcements", async () => { var p = await steam.NewsAsync(new(570, "Dota 2", null), null, default); return new { count = p.Items.Count, p.HasMore, p.Cursor }; });
        await Check("heybox-qr-challenge", async () => { var q = await api.QrAsync(default); return new { officialHost = new Uri(q.Url).Host, expiresInSeconds = (int)(q.Expires - DateTimeOffset.UtcNow).TotalSeconds }; });
        Console.WriteLine(JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, accountWrites = "not performed", results }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
