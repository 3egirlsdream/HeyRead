using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using HeyRead.Core;
using Xunit;
namespace HeyRead.Tests;

public sealed class RefinementTests
{
    [Fact]
    public void TranslationPreservesNestedMediaAndLinksWithoutMutatingOriginal()
    {
        ArticleBlock[] source = [new(BlockKind.Heading, "Update"), new(BlockKind.Quote, Children: [new(BlockKind.Paragraph, "Read more", Runs: [new("Read more", Url: "https://example.com/")]), new(BlockKind.Image, Url: "https://example.com/a.png", Caption: "A view")]), new(BlockKind.Game, "Game", AppId: 570)];
        var document = new TranslationDocument(source); Assert.Equal(new[] { "Update", "Read more", "A view" }, document.Input);
        var result = document.Apply(["更新", "阅读更多", "风景"]); Assert.Equal("更新", result[0].Text); Assert.Equal("Update", source[0].Text);
        Assert.Equal("https://example.com/", result[1].Children![0].Runs![1].Url); Assert.Equal("风景", result[1].Children![1].Caption); Assert.Equal(source[2], result[2]);
        Assert.Throws<InvalidOperationException>(() => document.Apply(["部分"])); Assert.Throws<InvalidOperationException>(() => document.Apply(["更新", "", "风景"]));
    }
    [Fact]
    public void NavigationRoundTripsScopeSelectionAndViewportAsBoundedMetadata()
    {
        var root = TemporaryRoot();
        try
        {
            var store = new LibraryStore(root); var story = new Story("1", Source.HEYBOX, "T", "Summary", Body: new string('x', 100000), Blocks: [new(BlockKind.Paragraph, "private body")]);
            store.Preferences.Navigation = new NavigationState("search", Query: "只狼", SearchScope: new("section", "7", "Games"), SelectedStory: story, ListOffset: 160, ArticleOffset: 420, CommentOffset: 200, CommentsMode: true, ShowDetail: true).Normalize(); store.SavePreferences();
            var restored = new LibraryStore(root).Preferences.Navigation!; Assert.Equal("只狼", restored.Query); Assert.Equal("7", restored.SearchScope?.Id); Assert.Equal(420, restored.ArticleOffset); Assert.True(restored.CommentsMode); Assert.Null(restored.SelectedStory?.Blocks); Assert.DoesNotContain("private body", File.ReadAllText(Path.Combine(root, "preferences.json")));
            Assert.Equal("discover", new NavigationState("news").Normalize().Page); Assert.Equal(0, new NavigationState(ArticleOffset: double.NaN).Normalize().ArticleOffset); Assert.Equal(120, new NavigationState(Query: new string('a', 200)).Normalize().Query.Length);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task ChangingAccountAfterHeadersRejectsDelayedBody()
    {
        var content = new DelayedContent(); using var api = new HeyReadApi(accountHandler: new RouteHandler(_ => new(HttpStatusCode.OK) { Content = content }));
        api.SetSession(new("pkey=first", "1", "One", "")); var pending = api.AccountListAsync("history", 0, ct: TestContext.Current.CancellationToken);
        await content.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); api.SetSession(new("pkey=second", "2", "Two", "")); content.Release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }
    [Fact]
    public async Task GameCardLoadsRealMetadataAndCachesOnlySuccessfulResponses()
    {
        var attempts = 0; using var api = new HeyReadApi(new RouteHandler(_ => new(HttpStatusCode.OK) { Content = new StringContent(++attempts == 1 ? "{\"570\":{\"success\":false}}" : "{\"570\":{\"success\":true,\"data\":{\"name\":\"Dota 2\",\"header_image\":\"https://cdn.example/header.jpg\",\"genres\":[{\"description\":\"Action\"}],\"developers\":[\"Valve\"]}}}") }));
        var service = new GameDetailsService(api); await Assert.ThrowsAsync<ApiException>(() => service.LoadAsync(570, TestContext.Current.CancellationToken));
        var details = await service.LoadAsync(570, TestContext.Current.CancellationToken); Assert.Equal("Valve", details.Developer); Assert.Equal("Action", details.Genres); Assert.Equal("Dota 2", details.ToGame().Name);
        Assert.Equal(details, await service.LoadAsync(570, TestContext.Current.CancellationToken)); Assert.Equal(2, attempts);
    }
    [Fact]
    public void ParserKeepsEmojiInlineGameCardsAndFeedVideo()
    {
        var blocks = ContentParser.Html("<p>Hi <img class='emoji' src='https://cdn.example/emoji.png' alt='[smile]'> there</p><div data-gameid='570'>Dota</div>");
        Assert.Contains(blocks.SelectMany(x => x.Runs ?? []), r => r.ImageUrl == "https://cdn.example/emoji.png"); Assert.Contains(blocks, b => b.Kind == BlockKind.Game && b.AppId == 570);
        Assert.DoesNotContain(blocks, b => b.Kind == BlockKind.Image);
    }
    [Fact]
    public void FontImportDoesNotOverwriteExistingFileAndCacheClearIncludesShares()
    {
        var root = TemporaryRoot(); var source = TemporaryRoot(); Directory.CreateDirectory(source);
        try
        {
            var store = new LibraryStore(root); var path = Path.Combine(source, "font.ttf"); File.WriteAllText(path, "first"); var first = store.ImportFont(path); File.WriteAllText(path, "second"); var second = store.ImportFont(path);
            Assert.NotEqual(first, second); Assert.Equal("first", File.ReadAllText(Path.Combine(root, "fonts", first)));
            Directory.CreateDirectory(Path.Combine(root, "cache", "share")); File.WriteAllText(Path.Combine(root, "cache", "share", "a.png"), "a"); Assert.True(store.CacheBytes() > 0); store.ClearCache(); Assert.Equal(0, store.CacheBytes());
        }
        finally { Directory.Delete(root, true); Directory.Delete(source, true); }
    }
    private static string TemporaryRoot() => Path.Combine(Path.GetTempPath(), "heyread-refinement-" + Guid.NewGuid());
    private sealed class DelayedContent : HttpContent
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context) { Started.TrySetResult(); await Release.Task; await stream.WriteAsync("{\"status\":\"ok\",\"result\":{\"links\":[]}}"u8.ToArray()); }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
}
public sealed class RouteHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request));
}
