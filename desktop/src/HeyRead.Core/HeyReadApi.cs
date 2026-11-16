using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
namespace HeyRead.Core;
/// <summary>Public, account, Steam and media transports have no shared cookies or default auth headers.</summary>
public sealed class HeyReadApi : IDisposable
{
    public const string OfficialHost = "api.xiaoheihe.cn";
    private readonly HttpClient _public;
    private readonly HttpClient _account;
    private readonly CookieContainer _cookies = new();
    private readonly object _sessionLock = new();
    private Session? _session;
    private int _sessionVersion;
    public Func<CaptchaChallenge, CancellationToken, Task<CaptchaProof>>? VerifyCaptcha { get; set; }
    public Session? Session { get { lock (_sessionLock) return _session; } }
    public Func<string> Gateway { get; set; } = () => "https://api.xiaoheihe.cn";
    public HeyReadApi(HttpMessageHandler? publicHandler = null, HttpMessageHandler? accountHandler = null)
    {
        _public = new(publicHandler ?? new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false });
        _account = new(accountHandler ?? new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false });
        _public.Timeout = _account.Timeout = TimeSpan.FromSeconds(20);
    }
    public void SetSession(Session? session) { lock (_sessionLock) { _session = session is { IsValid: true } ? session : null; _sessionVersion++; foreach (Cookie c in _cookies.GetCookies(new Uri("https://" + OfficialHost))) c.Expired = true; } }
    public static Uri SignedUri(string origin, string path, IReadOnlyDictionary<string, string>? parameters = null, string? accountId = null)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var baseUri) || baseUri.Scheme != "https" || !string.IsNullOrEmpty(baseUri.UserInfo) || baseUri.AbsolutePath != "/" || baseUri.Query.Length != 0) throw new ArgumentException("数据来源必须是有效的 HTTPS 站点");
        if (!path.StartsWith('/') || path.Contains('?') || path.Contains('#')) throw new ArgumentException("请求路径无效");
        var p = new Dictionary<string, string> { ["os_type"] = "web", ["app"] = "heybox", ["x_client_type"] = "web", ["x_os_type"] = accountId == null ? "Mac" : "Windows", ["x_app"] = accountId == null ? "heybox" : "heybox_website", ["version"] = "999.0.4" };
        if (accountId != null) { p["client_type"] = "web"; p["web_version"] = "2.5"; p["device_info"] = "Edge"; p["heybox_id"] = p["userid"] = accountId; }
        if (parameters != null) foreach (var (k, v) in parameters) p[k] = v;
        var sig = WebRequestSigner.Create(path); p["_time"] = sig.Time.ToString(); p["nonce"] = sig.Nonce; p["hkey"] = sig.Key;
        return new Uri(origin.TrimEnd('/') + path + "?" + Query(p));
    }
    public static string Query(IEnumerable<KeyValuePair<string, string>> p) => string.Join('&', p.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value)));
    public async Task<JsonObject> RequestAsync(string path, Dictionary<string, string>? parameters = null, bool account = false, Dictionary<string, string>? form = null, bool login = true, CancellationToken ct = default)
    {
        parameters = parameters == null ? null : new(parameters);
        Session? session; int version; lock (_sessionLock) { session = _session; version = _sessionVersion; }
        if (account && login && session == null) throw new ApiException("请先登录小黑盒账号", "login");
        var uri = SignedUri(account ? "https://" + OfficialHost : Gateway(), path, parameters, account && login ? session?.UserId : null);
        using var request = new HttpRequestMessage(form == null ? HttpMethod.Get : HttpMethod.Post, uri);
        request.Headers.UserAgent.ParseAdd("HeyRead/0.1 (macOS)"); request.Headers.Referrer = new("https://www.xiaoheihe.cn/"); request.Headers.Accept.Add(new("application/json"));
        if (account)
        {
            if (uri.Scheme != "https" || uri.Host != OfficialHost) throw new InvalidOperationException("已阻止账号跨域请求");
            request.Headers.CacheControl = new() { NoStore = true };
            var cookie = login ? session?.Cookie : _cookies.GetCookieHeader(uri);
            if (!string.IsNullOrWhiteSpace(cookie)) request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }
        if (form != null) request.Content = new FormUrlEncodedContent(form);
        using var response = await (account ? _account : _public).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (account && (int)response.StatusCode is >= 300 and < 400) throw new ApiException("账号服务重定向已阻止，请从官方入口验证", "show_captcha");
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new ApiException("登录已失效，请重新扫码", "relogin");
        response.EnsureSuccessStatusCode();
        if (account && version != _sessionVersion) throw new OperationCanceledException("账号已变化", ct);
        if (account && !login && response.Headers.TryGetValues("Set-Cookie", out var values)) foreach (var value in values) { try { _cookies.SetCookies(uri, value); } catch (CookieException) { } }
        var raw = await ReadLimitedAsync(response, 8 * 1024 * 1024, ct).ConfigureAwait(false);
        if (account && version != Volatile.Read(ref _sessionVersion)) throw new OperationCanceledException("账号已变化", ct);
        var root = JsonNode.Parse(raw) as JsonObject ?? throw new ApiException("响应格式无效");
        var status = root.S("status").ToLowerInvariant();
        if (status == "show_captcha" && !account && form == null && VerifyCaptcha != null && parameters?.ContainsKey("captcha_type") != true)
        {
            var appId = root.S("appid"); if (appId.Length == 0) appId = root["result"].S("appid");
            if (appId.Length is 0 or > 20 || !appId.All(char.IsAsciiDigit)) appId = path is "/bbs/app/profile/events" or "/bbs/app/profile/user/link/list/v2" or "/bbs/app/api/general/search/v1" ? "199251710" : "2061805035";
            var proof = await VerifyCaptcha(new(path, appId), ct); ct.ThrowIfCancellationRequested();
            var retry = parameters == null ? new Dictionary<string, string>() : new(parameters); proof.ApplyOnce(retry);
            return await RequestAsync(path, retry, false, ct: ct);
        }
        if (status is "login" or "relogin" || root.S("msg") is "login" or "relogin") throw new ApiException("登录已失效，请重新扫码", "relogin");
        if (status is not "ok" and not "success") throw new ApiException(status.Contains("captcha") || status.Contains("token") ? "服务要求额外验证，请通过官方入口完成后重试" : $"接口操作失败（{status}），请稍后重试", status);
        return root;
    }
    public static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response, int limit, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > limit) throw new IOException("响应过大");
        await using var stream = await response.Content.ReadAsStreamAsync(ct); using var output = new MemoryStream(); var buffer = new byte[16384];
        int n; while ((n = await stream.ReadAsync(buffer, ct)) != 0) { if (output.Length + n > limit) throw new IOException("响应过大"); await output.WriteAsync(buffer.AsMemory(0, n), ct); }
        return output.ToArray();
    }
    public async Task<Page<Story>> FeedAsync(int offset, bool dynamic = false, string channel = "all", CancellationToken ct = default)
    {
        var size = dynamic ? 30 : 10; var p = new Dictionary<string, string> { ["pull"] = "0", ["offset"] = offset.ToString(), ["dw"] = dynamic ? "604" : "304" }; if (!dynamic) p["limit"] = "10";
        var root = await RequestAsync("/bbs/app/feeds", p, ct: ct); var rows = root["result"].Array("links") ?? throw new ApiException("内容列表缺少 links");
        var items = rows.Where(x => channel == "all" || (x.I("use_concept_type") == 0 ? "news" : "community") == channel).Select(x => ContentParser.Story(x)).OfType<Story>().DistinctBy(x => x.Key).ToArray();
        return new(items, rows.Count > 0, offset + size);
    }
    public async Task<Page<Story>> SearchAsync(string query, int offset, Destination? scope = null, string? cursor = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 120) throw new ArgumentException("请输入 1–120 个字符");
        if (scope?.Kind == "topic") { var page = await DestinationAsync(scope, offset, cursor, ct: ct); return page with { Items = page.Items.Where(x => (x.Title + " " + x.Excerpt).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray() }; }
        var p = new Dictionary<string, string> { ["q"] = query, ["search_type"] = "link", ["offset"] = offset.ToString(), ["limit"] = "20" }; if (scope != null) p["topic_id"] = scope.Id;
        var root = await RequestAsync("/bbs/app/api/general/search/v1", p, ct: ct); var result = root["result"]; var rows = result.Array("items") ?? throw new ApiException("搜索响应缺少 items");
        var items = rows.Where(x => x.S("type") == "link").Select(x => ContentParser.Story(x?["info"])).OfType<Story>().DistinctBy(x => x.Key).ToArray();
        if (scope != null && items.Any(x => x.Sections is { Count: > 0 } && !x.Sections.Any(s => s.Id == scope.Id))) throw new ApiException("搜索返回的分区不符");
        return new(items, result.B("has_more") ?? result.B("hasMore") ?? rows.Count > 0, offset + 20);
    }
    public async Task<Page<Story>> DestinationAsync(Destination target, int offset, string? cursor, bool moments = false, CancellationToken ct = default)
    {
        var path = target.Kind switch { "author" => moments ? "/bbs/app/profile/events" : "/bbs/app/profile/user/link/list_v2", "section" => "/bbs/app/topic/feeds", "topic" => "/bbs/app/hashtag/feeds", _ => throw new ArgumentException("未知页面") };
        var p = new Dictionary<string, string> { [target.Kind switch { "author" => "userid", "section" => "topic_id", _ => "hashtag_id" }] = target.Id, ["offset"] = offset.ToString(), ["limit"] = "20", ["dw"] = "304" };
        if (cursor != null) p["lastval"] = cursor;
        if (target.Kind == "author") p["list_type"] = moments ? "moment" : "article";
        var root = await RequestAsync(path, p, ct: ct); return ParsePage(root, offset, 20);
    }
    public static Page<Story> ParsePage(JsonNode root, int offset, int size = 20)
    {
        var result = root["result"]; var rows = result.Array("links", "list", "history_visit", "visits", "history", "items", "rows", "records", "favorites", "collects", "link_list", "links_list", "moments", "data") ?? throw new ApiException("响应缺少内容列表");
        return new(rows.Select(x => ContentParser.Story(x)).OfType<Story>().DistinctBy(x => x.Key).ToArray(), result.B("has_more") ?? result.B("hasMore") ?? rows.Count >= size, offset + size, result.S("lastval"));
    }
    public async Task<Article> ArticleAsync(Story story, CancellationToken ct = default)
    {
        if (story.Source != Source.HEYBOX) return new(story with { Blocks = story.Blocks ?? ContentParser.Html(story.Body) }, []);
        var account = Session != null; JsonNode? tree = null; JsonNode? web = null; Exception? treeError = null;
        try { tree = await RequestAsync(account ? "/bbs/app/link/tree/v2" : "/bbs/app/link/tree", TreeParameters(story, 1), account, ct: ct); }
        catch (Exception e) when (e is not OperationCanceledException && e is not ApiException { Status: "relogin" }) { treeError = e; }
        try { web = await RequestAsync("/bbs/app/link/web/view", new() { ["link_id"] = story.Id, ["return_json"] = "1", ["in_topic"] = "1" }, account, ct: ct); }
        catch (Exception e) when (e is not OperationCanceledException && e is not ApiException { Status: "relogin" }) { if (tree == null) throw new ApiException(e.Message + "；" + treeError?.Message); }
        var link = tree?["result"]?["link"]; var webLink = web?["result"]?["link"]; var body = link?["body"];
        var blocks = ContentParser.Blocks(webLink?["content"]);
        if (blocks.Count == 0) blocks = ContentParser.Blocks(body?["content"] ?? link?["content"]);
        if (blocks.Count == 0) blocks = ContentParser.Html(body.S("text", "description", "content").Length > 0 ? body.S("text", "description", "content") : story.Body);
        var video = ContentParser.PublicVideo(webLink) ?? ContentParser.PublicVideo(link) ?? ContentParser.PublicVideo(body) ?? story.FeedVideo;
        if (video != null && !blocks.Any(b => b.Kind == BlockKind.Video && b.Url == video.Url)) blocks = blocks.Concat([video]).ToArray();
        var profile = ContentParser.Profile(body?["user"] ?? webLink?["poster"]) ?? story.Profile;
        var stats = link?["stats"]; var comments = tree?["result"]?["comment"];
        foreach (var returned in new[] { link, webLink }) if (returned.S("linkid", "link_id") is { Length: > 0 } returnedId && returnedId != story.Id) throw new ApiException("返回文章与当前内容不一致");
        var authCode = link.S("auth_code"); if (authCode.Length == 0) authCode = (tree?["result"]).S("auth_code"); if (authCode.Length == 0) authCode = tree.S("auth_code");
        var followed = FollowStatus(stats.S("follow_status"));
        if (followed == null && profile?.Id is { Length: > 0 } authorId && (webLink?["poster"]).S("userid", "user_id", "heybox_id") == authorId) followed = FollowStatus((web?["result"]?["current_user"]).S("follow_status"));
        return new(story with { Blocks = blocks, Profile = profile, Author = profile?.Name ?? story.Author, Sections = ContentParser.Labels(webLink?["topics"], "topic_id").Count > 0 ? ContentParser.Labels(webLink?["topics"], "topic_id") : story.Sections, Topics = ContentParser.Labels(webLink?["hashtags"], "hashtag_id").Count > 0 ? ContentParser.Labels(webLink?["hashtags"], "hashtag_id") : story.Topics, Notice = link.I("view_limit") == 1 ? "当前显示公开内容，更多内容可打开原文查看" : webLink == null ? "暂未获取完整正文，当前显示可用内容" : null }, ContentParser.Comments(comments.Array("comments")), comments?["comment_config"].B("has_more_floors") ?? comments.Array("comments")?.Count >= 20, authCode.Length > 0 ? authCode : null, stats.B("is_award_link") ?? stats.B("is_award") ?? stats.B("is_liked"), stats.B("is_favour"), followed, LikeCount: stats.OptionalInt("link_award_num", "award_num", "like_num"), CommentCount: stats.OptionalInt("comment_num") ?? link.OptionalInt("comment_num"), CommentsError: account && comments.Array("comments") == null ? "评论暂不可用，请重试" : null);
    }
    private static bool? FollowStatus(string status) => status.Trim().ToLowerInvariant() switch { "0" or "2" or "false" => false, "1" or "3" or "true" => true, _ => null };
    public async Task<bool> AuthorFollowStatusAsync(Story story, string authorId, CancellationToken ct)
    {
        var parameters = new Dictionary<string, string> { ["link_id"] = story.Id, ["return_json"] = "1", ["in_topic"] = "1" }; if (!string.IsNullOrEmpty(story.HSrc)) parameters["h_src"] = story.HSrc;
        var json = await RequestAsync("/bbs/app/link/web/view", parameters, true, ct: ct); var result = json["result"];
        if ((result?["link"]?["poster"]).S("userid", "user_id", "heybox_id") != authorId) throw new ApiException("返回作者与当前文章不一致");
        return FollowStatus((result?["current_user"]).S("follow_status")) ?? throw new ApiException("官方暂未返回关注关系，请重试");
    }
    private static Dictionary<string, string> TreeParameters(Story story, int page) { var p = new Dictionary<string, string> { ["link_id"] = story.Id, ["page"] = page.ToString(), ["limit"] = "20", ["is_first"] = page == 1 ? "1" : "0", ["sort_filter"] = "hot", ["owner_only"] = "0", ["hide_cy"] = "0", ["check_commented"] = "0", ["dw"] = "304" }; if (!string.IsNullOrEmpty(story.HSrc)) p["h_src"] = story.HSrc; return p; }
    public async Task<Page<Comment>> CommentsAsync(Story story, int page, string? rootId = null, string? cursor = null, CancellationToken ct = default)
    {
        var p = rootId == null ? TreeParameters(story, page) : new Dictionary<string, string> { ["root_comment_id"] = rootId, ["hide_cy"] = "0" }; if (cursor != null) p["lastval"] = cursor;
        var json = await RequestAsync(rootId == null ? "/bbs/app/link/tree/v2" : "/bbs/app/comment/sub/comments", p, true, ct: ct);
        var result = rootId == null ? json["result"]?["comment"] : json["result"]; var rows = result.Array("comments", "comment", "list", "replies", "items");
        return new(ContentParser.Comments(rows, rootId), result?["comment_config"].B("has_more_floors") ?? result.B("has_more") ?? rows?.Count >= 20, page + 1, result.S("lastval"));
    }
    public async Task<QrChallenge> QrAsync(CancellationToken ct)
    {
        var root = await RequestAsync("/account/get_qrcode_url/", new() { ["app"] = "web", ["heybox_id"] = "" }, true, login: false, ct: ct); var result = root["result"];
        var url = result.S("qr_url"); if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != OfficialHost) throw new ApiException("二维码返回了非官方地址");
        var key = uri.Query.TrimStart('?').Split('&').Select(x => x.Split('=', 2)).Where(x => x.Length == 2 && x[0] == "qr").Select(x => Uri.UnescapeDataString(x[1])).FirstOrDefault() ?? result.S("qr");
        if (key.Length == 0) throw new ApiException("二维码缺少校验参数"); return new(url, key, DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(result.I("expire", 300), 1, 300)));
    }
    public async Task<QrPoll> PollAsync(QrChallenge qr, CancellationToken ct)
    {
        if (DateTimeOffset.UtcNow >= qr.Expires) return new("expired");
        var root = await RequestAsync("/account/qr_state/", new() { ["app"] = "web", ["qr"] = qr.Key }, true, login: false, ct: ct); var r = root["result"]; var error = r.S("error").ToLowerInvariant();
        if (error != "ok") { if (error is "login" or "relogin" or "lack_token" or "show_captcha") throw new ApiException("登录暂不可用，请从官方入口验证后重试", error); return new(error == "ready" ? "ready" : error == "cancel" ? "cancelled" : "waiting"); }
        var account = r?["account_detail"] ?? r?["user"]; var cookie = r.S("cookie", "Cookie"); if (cookie.Length == 0) cookie = _cookies.GetCookieHeader(new("https://" + OfficialHost));
        var pkey = r.S("pkey", "user_pkey", "x_pkey"); if (pkey.Length == 0) pkey = account.S("pkey", "user_pkey", "x_pkey"); if (pkey.Length > 0) cookie += "; pkey=" + pkey;
        var id = r.S("heyboxid", "userid", "heybox_id", "user_heybox_id", "x_heybox_id"); if (id.Length == 0) id = account.S("heyboxid", "userid", "heybox_id");
        var pairs = cookie.Split(';').Select(x => x.Trim().Split('=', 2)).Where(x => x.Length == 2 && !new[] { "secure", "path", "domain", "expires", "httponly", "samesite", "max-age" }.Contains(x[0].ToLowerInvariant())).GroupBy(x => x[0]).Select(x => x.Last()).ToDictionary(x => x[0], x => x[1]);
        if (id.Length == 0) foreach (var key in new[] { "heybox_id", "user_heybox_id", "x_heybox_id", "userid", "heyboxid" }) if (pairs.TryGetValue(key, out var v)) { id = v; break; }
        cookie = string.Join("; ", pairs.Select(x => x.Key + "=" + x.Value));
        var session = new Session(cookie, id, r.S("nickname", "username") is { Length: > 0 } nick ? nick : account.S("nickname", "username"), r.S("avatar", "avatar_url") is { Length: > 0 } avatar ? avatar : account.S("avatar", "avatar_url"));
        if (!session.IsValid) throw new ApiException("未返回完整账号凭据，请重新扫码"); return new("loggedin", session);
    }
    public async Task<IReadOnlyList<Folder>> FoldersAsync(CancellationToken ct)
    {
        var root = await RequestAsync("/bbs/app/profile/fav/tab_list", account: true, ct: ct); var rows = root["result"].Array("tab_list", "tabs", "folders", "folder_list", "list", "items", "data");
        return rows?.Select(x => x?["folder"] ?? x?["folder_info"] ?? x).Select(x => new Folder(x.S("folder_id", "folderid", "fav_folder_id", "id", "fid"), x.S("folder_name", "name", "title", "tab_name"))).Where(x => x.Id.Length > 0).ToArray() ?? [];
    }
    public async Task<Page<Story>> AccountListAsync(string kind, int offset, string? folderId = null, string? cursor = null, CancellationToken ct = default)
    {
        var size = kind == "following" ? 30 : 20; var p = new Dictionary<string, string> { ["offset"] = offset.ToString(), ["limit"] = size.ToString() };
        var path = kind switch { "favorites" => "/bbs/app/profile/fav/folder/v2/links", "following" => "/bbs/app/profile/subscribed/events", _ => "/bbs/app/profile/history/visit" };
        if (kind == "favorites") { p["enable_new_style_collect"] = "1"; if (folderId != null) p["folder_id"] = folderId; } else if (kind == "history") p["type"] = "link";
        if (cursor != null) p["lastval"] = cursor;
        var root = await RequestAsync(path, p, true, ct: ct); if (root["result"]?["login_card"] is JsonObject { Count: > 0 }) throw new ApiException("关注内容暂不可用，请确认账号状态", "relogin");
        return ParsePage(root, offset, size);
    }
    public async Task<IReadOnlyList<Author>> FollowingAsync(CancellationToken ct)
    {
        var root = await RequestAsync("/bbs/app/profile/following/simple_list", new() { ["userid"] = Session?.UserId ?? "" }, true, ct: ct);
        var rows = root["result"].Array("follow_list") ?? throw new ApiException("关注用户响应缺少 follow_list");
        return rows.Select(x => ContentParser.Profile(x?["user"] ?? x)).OfType<Author>().Where(x => x.Id.Length > 0).ToArray();
    }
    public async Task<AuthorPage> ProfileAsync(string userId, CancellationToken ct)
    {
        var root = await RequestAsync("/bbs/app/profile/user/profile", new() { ["userid"] = userId }, ct: ct);
        var detail = root["result"]?["account_detail"] ?? throw new ApiException("作者资料暂不可用"); var stats = detail["bbs_info"];
        return new(ContentParser.Profile(detail) ?? new(userId), detail.S("signature", "bio"), Count(stats, "follow_num"), Count(stats, "fan_num"), Count(stats, "awd_num") is { } likes && Count(stats, "be_favoured_num") is { } saved ? likes + saved : null);
    }
    private static long? Count(JsonNode? value, string key) => long.TryParse(value.S(key), out var count) && count >= 0 ? count : null;
    public Task<JsonObject> SocialAsync(Story story, string action, bool enabled, string? text = null, string? authCode = null, Comment? reply = null, CancellationToken ct = default)
    {
        if (story.Source != Source.HEYBOX) throw new ArgumentException("此操作仅适用于小黑盒内容");
        var p = new Dictionary<string, string>(); if (!string.IsNullOrEmpty(story.HSrc)) p["h_src"] = story.HSrc;
        var form = new Dictionary<string, string>(); string path;
        switch (action)
        {
            case "favorite": path = "/bbs/app/link/favour"; form["link_id"] = story.Id; form["favour_type"] = enabled ? "1" : "2"; break;
            case "like": path = "/bbs/app/profile/award/link"; form["link_id"] = story.Id; form["award_type"] = enabled ? "1" : "0"; break;
            case "follow": path = enabled ? "/bbs/app/profile/follow/user" : "/bbs/app/profile/follow/user/cancel"; form["following_id"] = story.Profile?.Id is { Length: > 0 } id ? id : throw new ArgumentException("作者未提供 ID"); break;
            case "support": path = "/bbs/app/comment/support"; form["comment_id"] = reply?.Id ?? throw new ArgumentException("评论 ID 缺失"); form["support_type"] = enabled ? "1" : "2"; break;
            case "comment":
                if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("评论内容不能为空"); path = "/bbs/app/comment/create"; form["link_id"] = story.Id; form["text"] = text.Trim(); form["imgs"] = ""; form["is_cy"] = "0"; if (authCode != null) p["auth_code"] = authCode;
                if (reply != null) { if (reply.Id.Length == 0 || reply.RootId.Length == 0) throw new ArgumentException("回复目标不完整"); form["root_id"] = reply.RootId; form["reply_id"] = reply.Id; }
                break;
            default: throw new ArgumentException("未知互动操作");
        }
        return RequestAsync(path, p, true, form, ct: ct);
    }
    public async Task<JsonNode> PublicJsonAsync(string url, CancellationToken ct)
    {
        if (J.Https(url) == null) throw new ArgumentException("需要 HTTPS 地址"); using var request = new HttpRequestMessage(HttpMethod.Get, url); request.Headers.UserAgent.ParseAdd("HeyRead/0.1 (macOS)");
        using var response = await _public.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); response.EnsureSuccessStatusCode(); return JsonNode.Parse(await ReadLimitedAsync(response, 8 * 1024 * 1024, ct)) ?? throw new ApiException("响应为空");
    }
    public void Dispose() { _public.Dispose(); _account.Dispose(); }
}
