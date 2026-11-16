using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using HeyRead.Core;
using Xunit;
namespace HeyRead.Tests;

public sealed class FakeHandler(string body) : HttpMessageHandler
{
    public List<(Uri Uri, string? Cookie, string Method, string? Form)> Requests { get; } = [];
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Requests.Add((request.RequestUri!, request.Headers.TryGetValues("Cookie", out var c) ? string.Join(";", c) : null, request.Method.Method, request.Content == null ? null : await request.Content.ReadAsStringAsync(ct))); return new(Status) { Content = new StringContent(body), RequestMessage = request }; }
}
public sealed class CoreTests
{
    [Fact]
    public void HistoryUnwrapsContentAndNeverUsesReaderIdentity()
    {
        var root = JsonNode.Parse("""{"status":"ok","result":{"history_visit":[{"id":"record-1","userid":"reader","content":{"linkid":"post-1","title":"T","poster":{"userid":"writer","username":"Author"}}}]}}""")!;
        var page = HeyReadApi.ParsePage(root, 0); var item = Assert.Single(page.Items); Assert.Equal("post-1", item.Id); Assert.Equal("writer", item.Profile?.Id);
    }
    [Fact]
    public void CaptchaProofIsSingleUseAndTokensAreRedacted()
    {
        var proof = CaptchaProof.Parse("""{"ret":0,"ticket":"private-ticket","randstr":"private-rand"}"""); Assert.DoesNotContain("private", proof.ToString()); var p = new Dictionary<string, string>(); proof.ApplyOnce(p); Assert.Equal("confirm", p["captcha_type"]); Assert.Throws<InvalidOperationException>(() => proof.ApplyOnce(new()));
        Assert.Throws<ApiException>(() => CaptchaProof.Parse("""{"ret":0,"ticket":"trerror_invalid","randstr":"x"}"""));
    }
    [Fact]
    public void SignerMatchesUpstreamVerifiedVector()
    {
        Assert.Equal("V2V1Z67", WebRequestSigner.Sign("/bbs/app/feeds", 1700000000, "ABCDEF0123456789ABCDEF0123456789"));
        Assert.Equal(WebRequestSigner.Sign("/bbs/app/feeds", 1700000000, "ABCDEF0123456789ABCDEF0123456789"), WebRequestSigner.Sign("bbs/app/feeds/", 1700000000, "ABCDEF0123456789ABCDEF0123456789"));
    }
    [Fact]
    public void SteamMarkupAndInlineFormattingSurviveParsing()
    {
        var blocks = ContentParser.Html(SteamService.SteamMarkup("[h2]Update[/h2]<p>A <b>bold</b> <a href='https://example.com'>link</a></p>[img]{STEAM_CLAN_IMAGE}/123/test.jpg[/img]"));
        Assert.Contains(blocks, b => b.Kind == BlockKind.Heading);
        Assert.Contains(blocks, b => b.Kind == BlockKind.Image && b.Url == "https://clan.cloudflare.steamstatic.com/images/123/test.jpg");
        Assert.Contains(blocks.SelectMany(b => b.Runs ?? []), r => r.Bold && r.Text == "bold");
        Assert.Contains(blocks.SelectMany(b => b.Runs ?? []), r => r.Url == "https://example.com/");
    }
    [Fact]
    public async Task AccountCookiesCannotReachGatewaySteamOrPublicFeeds()
    {
        var publicTransport = new FakeHandler("{\"status\":\"ok\",\"result\":{\"links\":[]}}"); var accountTransport = new FakeHandler("{\"status\":\"ok\",\"result\":{\"links\":[]}}");
        using var api = new HeyReadApi(publicTransport, accountTransport) { Gateway = () => "https://example.com" };
        api.SetSession(new("pkey=private; userid=42", "42", "Test", ""));
        await api.FeedAsync(0, ct: TestContext.Current.CancellationToken); await api.AccountListAsync("history", 0, ct: TestContext.Current.CancellationToken); await api.PublicJsonAsync("https://store.steampowered.com/test", TestContext.Current.CancellationToken);
        Assert.All(publicTransport.Requests, r => Assert.Null(r.Cookie)); var r = Assert.Single(accountTransport.Requests); Assert.Equal(HeyReadApi.OfficialHost, r.Uri.Host); Assert.Contains("pkey=private", r.Cookie);
    }
    [Fact]
    public async Task AccountRedirectsFailClosed()
    {
        var handler = new FakeHandler("{}") { Status = HttpStatusCode.Redirect }; using var api = new HeyReadApi(accountHandler: handler); api.SetSession(new("pkey=test", "1", "Test", ""));
        var e = await Assert.ThrowsAsync<ApiException>(() => api.AccountListAsync("history", 0, ct: TestContext.Current.CancellationToken)); Assert.Equal("show_captcha", e.Status); Assert.Single(handler.Requests);
    }
    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://u:p@example.com")]
    [InlineData("https://example.com/path")]
    public void GatewayRejectsInsecureOrAmbiguousOrigins(string gateway) => Assert.Throws<ArgumentException>(() => HeyReadApi.SignedUri(gateway, "/bbs/app/feeds"));
    [Fact]
    public async Task ReplyProtocolPreservesRootAndTargetAndRequiresHeybox()
    {
        var handler = new FakeHandler("{\"status\":\"ok\"}"); using var api = new HeyReadApi(accountHandler: handler); api.SetSession(new("pkey=test", "42", "T", ""));
        var story = new Story("7", Source.HEYBOX, "Title", ""); var comment = new Comment("child", "root", "text", new(), "", 0, false, [], []);
        await api.SocialAsync(story, "comment", true, "hello", "auth", comment, TestContext.Current.CancellationToken); var request = Assert.Single(handler.Requests); Assert.Equal("POST", request.Method); Assert.Contains("root_id=root", request.Form); Assert.Contains("reply_id=child", request.Form); Assert.Contains("imgs=", request.Form);
        await Assert.ThrowsAsync<ArgumentException>(() => api.SocialAsync(story with { Source = Source.STEAM }, "like", true, ct: TestContext.Current.CancellationToken));
    }
    [Fact]
    public void AndroidRulesRoundTripAndCanonicalizeDuplicateIds()
    {
        var raw = "{\"format\":\"heyread-content-blocking\",\"version\":1,\"rules\":[{\"kind\":\"AUTHOR\",\"value\":\"0012\",\"label\":\"作者\",\"enabled\":true,\"source\":\"HEYBOX\"},{\"kind\":\"AUTHOR\",\"value\":\"12\",\"label\":\"作者\",\"enabled\":true,\"source\":\"HEYBOX\"}]}";
        var import = BlockingCodec.Import(Encoding.UTF8.GetBytes(raw)); Assert.Equal(1, import.Duplicates); Assert.Equal("12", Assert.Single(import.Rules).Value); Assert.Equal(import.Rules, BlockingCodec.Import(BlockingCodec.Export(import.Rules)).Rules);
    }
    [Theory]
    [InlineData("{\"format\":\"heyread-content-blocking\",\"format\":\"heyread-content-blocking\",\"version\":1,\"rules\":[]}")]
    [InlineData("{\"format\":\"heyread-content-blocking\",\"version\":2,\"rules\":[]}")]
    public void InvalidRulesAreRejected(string raw) => Assert.ThrowsAny<Exception>(() => BlockingCodec.Import(Encoding.UTF8.GetBytes(raw)));
    [Fact]
    public void BlockingDoesNotSearchArticleBody()
    {
        var r = new BlockRule(RuleKind.KEYWORD, "hidden"); Assert.False(r.Matches(new("1", Source.HEYBOX, "Title", "Summary", Body: "hidden"))); Assert.True(r.Matches(new("1", Source.STEAM, "HIDDEN", "")));
    }
    [Fact]
    public void ParserHandlesNestedCloudRowsCommentsAndUnsafeMedia()
    {
        var story = ContentParser.Story(JsonNode.Parse("{\"record\":{\"link\":{\"linkid\":7,\"title\":\"<b>T</b>\",\"user\":{\"userid\":42,\"username\":\"A\"}}}}")); Assert.Equal("7", story?.Id); Assert.Equal("42", story?.Profile?.Id);
        var comments = ContentParser.Comments(JsonNode.Parse("[{\"comment\":[{\"commentid\":\"root\",\"text\":\"a\"},{\"commentid\":\"child\",\"text\":\"b\"}]}]")!.AsArray()); Assert.Equal("root", Assert.Single(Assert.Single(comments).Replies).RootId);
        var blocks = ContentParser.Html("<script>bad()</script><h2>Heading</h2><p>body</p><img src='http://unsafe/image'><img src='https://cdn.example.com/image'><blockquote><p>quote</p></blockquote>"); Assert.Single(blocks, b => b.Kind == BlockKind.Image); Assert.DoesNotContain(blocks, b => b.Text.Contains("bad()")); Assert.Contains(blocks, b => b.Kind == BlockKind.Quote);
    }
    [Fact]
    public void StoreKeepsDraftsOffsetsAndIndependentTestRoots()
    {
        var root = Path.Combine(Path.GetTempPath(), "heyread-test-" + Guid.NewGuid()); var s = new LibraryStore(root); var story = new Story("1", Source.HEYBOX, "T", "S"); s.SavePreferences(); s.Visit(story, 125, 250); s.SetDraft("1:reply", "unsent"); s.Search("word", new("section", "2", "Games"));
        var restored = new LibraryStore(root); Assert.Equal("unsent", restored.Drafts["1:reply"]); Assert.Equal(250, Assert.Single(restored.History).CommentOffset); Assert.Equal("2", restored.Searches[0].Scope?.Id); Assert.DoesNotContain("pkey", File.ReadAllText(Path.Combine(root, "preferences.json"))); Directory.Delete(root, true);
    }
}
