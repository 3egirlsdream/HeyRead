using System.Net;
using Avalonia.Headless.XUnit;
using HeyRead.Core;
using HeyRead.Desktop.Platform;
using HeyRead.Desktop.ViewModels;
using Xunit;
namespace HeyRead.Tests;

public sealed class NavigationTranslationTests
{
    [AvaloniaFact]
    public async Task BackForwardAndRestartRestoreSearchScopeAndReadingPosition()
    {
        var root = Temp();
        try
        {
            using var api = new HeyReadApi(new FakeHandler("{\"status\":\"ok\",\"result\":{\"links\":[]}}")); var store = new LibraryStore(root);
            using var vm = Create(api, store, new ControlledTranslation());
            vm.Query = "Sekiro"; vm.SearchScope = new("section", "42", "Games"); await vm.SearchAsync();
            var story = new Story("steam-one", Source.STEAM, "Update", "Summary", Blocks: [new(BlockKind.Paragraph, "First")]); await vm.SelectAsync(story); vm.SaveViewport(120, 420, 0, true);
            vm.Query = "New search"; await vm.SearchAsync(); await vm.BackAsync(); Assert.Equal("Sekiro", vm.Query);
            await vm.NavigateAsync("settings"); await vm.BackAsync();
            Assert.Equal("search", vm.Page); Assert.Equal("Sekiro", vm.Query); Assert.Equal("42", vm.SearchScope?.Id); Assert.Equal(420, vm.CurrentViewport.ArticleOffset); Assert.Equal(story.Key, vm.Article?.Story.Key);
            await vm.BackAsync(true); Assert.Equal("settings", vm.Page); await vm.BackAsync(); vm.SaveNavigation();
            using var restored = Create(api, new LibraryStore(root), new ControlledTranslation()); await restored.InitializeAsync(); Assert.Equal(story.Key, restored.Article?.Story.Key); Assert.True(restored.CurrentViewport.ShowDetail); Assert.Equal(120, restored.CurrentViewport.ListOffset);
        }
        finally { Directory.Delete(root, true); }
    }
    [AvaloniaFact]
    public async Task TranslationCanRetryCancelAndReuseCompletedDocument()
    {
        var root = Temp();
        try
        {
            using var api = new HeyReadApi(); var translation = new ControlledTranslation(); using var vm = Create(api, new(root), translation);
            var story = new Story("n", Source.STEAM, "Update", "", Blocks: [new(BlockKind.Paragraph, "Original")]); await vm.SelectAsync(story);
            var failed = vm.TranslateAsync(); translation.Pending.Dequeue().SetException(new IOException("offline")); await failed; Assert.Equal("failed", vm.TranslationState); Assert.Contains("offline", vm.TranslationError);
            var canceled = vm.TranslateAsync(); var canceledSource = translation.Pending.Dequeue(); vm.CancelTranslation(); canceledSource.SetResult(["Stale"]); await canceled; Assert.Null(vm.TranslatedBlocks); Assert.False(vm.Translating);
            var success = vm.TranslateAsync(); translation.Pending.Dequeue().SetResult(["译文"]); await success; Assert.Equal("译文", vm.TranslatedBlocks![0].Text); Assert.Equal("translated", vm.TranslationMode);
            await vm.SelectAsync(story with { Id = "other" }); await vm.SelectAsync(story); await vm.TranslateAsync(); Assert.Equal(3, translation.Requests); Assert.Equal("ready", vm.TranslationState);
        }
        finally { Directory.Delete(root, true); }
    }
    [AvaloniaFact]
    public async Task LateTranslationCannotReplaceAnotherArticleOrAccount()
    {
        var root = Temp();
        try
        {
            using var api = new HeyReadApi(); var translation = new ControlledTranslation(); using var vm = Create(api, new(root), translation);
            await vm.SelectAsync(new("n", Source.STEAM, "Update", "", Blocks: [new(BlockKind.Paragraph, "Original")]));
            var task = vm.TranslateAsync(); var pending = translation.Pending.Dequeue(); await vm.SelectAsync(new("next", Source.STEAM, "Next", "", Blocks: [new(BlockKind.Paragraph, "Next")])); pending.SetResult(["Old result"]); await task;
            Assert.Equal("next", vm.Article?.Story.Id); Assert.Null(vm.TranslatedBlocks); Assert.False(vm.Translating);
        }
        finally { Directory.Delete(root, true); }
    }
    [AvaloniaFact]
    public async Task ConcurrentSocialActionsMergeAndNestedCommentSupportToggles()
    {
        var root = Temp();
        try
        {
            var body = """{"status":"ok","result":{"link":{"stats":{"is_award":false,"is_favour":false,"link_award_num":3},"body":{"content":"<p>Body</p>"},"content":"<p>Body</p>"},"comment":{"comments":[{"commentid":"root","text":"Root","sub_comments":[{"commentid":"child","text":"Reply","support_num":2}]}]}}}""";
            var gates = new Queue<TaskCompletionSource<HttpResponseMessage>>();
            using var api = new HeyReadApi(accountHandler: new AsyncHandler(request =>
            {
                if (request.Method == HttpMethod.Get) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
                var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously); gates.Enqueue(pending); return pending.Task;
            }));
            api.SetSession(new("pkey=fixture", "42", "Fixture", "")); using var vm = Create(api, new(root), new ControlledTranslation());
            await vm.SelectAsync(new("7", Source.HEYBOX, "Post", ""));
            var like = vm.SocialAsync("like"); var favorite = vm.SocialAsync("favorite"); gates.Dequeue().SetResult(Ok()); gates.Dequeue().SetResult(Ok()); await Task.WhenAll(like, favorite);
            Assert.True(vm.Article!.Liked); Assert.True(vm.Article.Favorited); Assert.Equal(4, vm.Article.LikeCount);
            var reply = Assert.Single(Assert.Single(vm.Comments).Replies); var support = vm.SocialAsync("support", reply); gates.Dequeue().SetResult(Ok()); await support;
            reply = Assert.Single(Assert.Single(vm.Comments).Replies); Assert.True(reply.Supported); Assert.Equal(3, reply.Supports);
            var undo = vm.SocialAsync("support", reply); gates.Dequeue().SetResult(Ok()); await undo; Assert.False(vm.Comments[0].Replies[0].Supported); Assert.Equal(2, vm.Comments[0].Replies[0].Supports);
        }
        finally { Directory.Delete(root, true); }
        static HttpResponseMessage Ok() => new(HttpStatusCode.OK) { Content = new StringContent("{\"status\":\"ok\"}") };
    }
    private sealed class AsyncHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => action(request);
    }
    [AvaloniaFact]
    public async Task BlockedNavigationDoesNotFetchHiddenContentUntilExplicitReveal()
    {
        var root = Temp();
        try
        {
            var handler = new FakeHandler("{\"status\":\"ok\",\"result\":{\"link\":{\"content\":\"<p>Content</p>\"}}}"); using var api = new HeyReadApi(handler);
            var store = new LibraryStore(root); store.Preferences.Rules.Add(new(RuleKind.KEYWORD, "Hidden")); using var vm = Create(api, store, new ControlledTranslation());
            await vm.SelectAsync(new("1", Source.HEYBOX, "Hidden title", "Summary")); Assert.True(vm.IsArticleBlocked); Assert.Empty(handler.Requests); Assert.False(vm.DetailLoading);
            vm.Reveal = true; await vm.RetryDetailAsync(); Assert.False(vm.IsArticleBlocked); Assert.Equal(2, handler.Requests.Count); Assert.Contains(vm.Article!.Story.Blocks!, b => b.Text == "Content");
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void LocalesHaveTheSameKeysAndFormatArguments()
    {
        var assembly = typeof(HeyRead.Desktop.Resources.Strings).Assembly; Dictionary<string, string>? baseline = null;
        foreach (var locale in new[] { "zh-CN", "zh-TW", "en", "fr" })
        {
            using var stream = assembly.GetManifestResourceStream("HeyRead.Desktop.Resources." + locale + ".json")!;
            var values = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
            baseline ??= values; Assert.Equal(baseline.Keys.Order(), values.Keys.Order());
            foreach (var pair in values) { var count = System.Text.RegularExpressions.Regex.Matches(baseline[pair.Key], @"\{\d+").Count; Assert.Equal(count, System.Text.RegularExpressions.Regex.Matches(pair.Value, @"\{\d+").Count); }
        }
    }
    private static string Temp() => Path.Combine(Path.GetTempPath(), "heyread-navigation-" + Guid.NewGuid());
    private static MainViewModel Create(HeyReadApi api, LibraryStore store, ITranslationService translation) => new(api, new(api), store, new MemorySessionStore(), translation);
    private sealed class ControlledTranslation : ITranslationService
    {
        public Queue<TaskCompletionSource<string[]>> Pending { get; } = [];
        public int Requests { get; private set; }
        public Task<string[]> TranslateAsync(string[] text, string target, CancellationToken ct) { Requests++; var source = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously); Pending.Enqueue(source); return source.Task; }
    }
}
