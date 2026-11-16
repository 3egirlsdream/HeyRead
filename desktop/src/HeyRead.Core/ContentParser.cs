using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using System.Text.Json.Nodes;
namespace HeyRead.Core;

public static class ContentParser
{
    public static string Text(string html) => new HtmlParser().ParseDocument(html).Body?.TextContent.Trim() ?? "";
    public static Story? Story(JsonNode? row, int depth = 0)
    {
        if (row is not JsonObject || depth > 4) return null;
        foreach (var key in new[] { "link", "link_info", "link_detail", "moment", "post", "favorite", "fav", "record", "target", "obj", "content", "source", "data", "item" }) if (row[key] is JsonObject wrapped) return Story(wrapped, depth + 1);
        var id = row.S("linkid", "link_id", "linkId", "id", "post_id", "moment_id");
        var body = Text(row.S("description", "text", "content", "body", "summary", "abstract"));
        var title = Text(row.S("title", "link_title")); if (title.Length == 0) title = body[..Math.Min(48, body.Length)];
        if (id.Length == 0 || title.Length == 0 || row.I("content_type", -1) == 18) return null;
        var candidates = new[] { row["user"], row["poster"], row["user_info"] }.OfType<JsonObject>().ToArray();
        JsonNode? user = candidates.FirstOrDefault(x => x.TextValue("username", "nickname", "display_name").Length > 0) ?? candidates.FirstOrDefault();
        var fallbackAuthor = row["author"];
        var useFallback = user.TextValue("username", "nickname", "display_name").Length == 0 && row.TextValue("username").Length == 0 && fallbackAuthor.TextValue("nickname").Length > 0;
        var profile = useFallback ? new Author("", fallbackAuthor.TextValue("nickname"), fallbackAuthor.FirstHttps("avatar")) : Profile(user, row.S("ip_location"));
        var image = row.FirstHttps("cover", "img", "image") ?? row.Array("imgs", "images", "image_list")?.Select(x => x is JsonValue ? J.Https(x.ToString()) : x.FirstHttps("url", "src", "image_url")).FirstOrDefault(x => x != null);
        var published = row.L("create_at"); var date = row.S("create_str", "date");
        if (date.Length == 0 && published > 0) date = DateTimeOffset.FromUnixTimeSeconds(published).ToLocalTime().ToString("yyyy-MM-dd");
        var author = profile?.Name; if (string.IsNullOrEmpty(author)) author = row.TextValue("username");
        return new(id, Source.HEYBOX, title, body[..Math.Min(160, body.Length)], body, author ?? "", date, image, J.Https(row.S("share_url")) ?? $"https://www.xiaoheihe.cn/app/bbs/link/{Uri.EscapeDataString(id)}", PublishedAt: published, HSrc: row.S("h_src"), Profile: profile, Sections: Labels(row["topics"], "topic_id"), Topics: Labels(row["hashtags"], "hashtag_id"));
    }
    public static Author? Profile(JsonNode? user, string? location = null) => user is JsonObject ? new(user.S("userid", "user_id", "heybox_id", "userId"), user.TextValue("username", "nickname", "display_name"), user.FirstHttps("avatar", "avartar", "avatar_url"), location) : null;
    public static IReadOnlyList<Label> Labels(JsonNode? array, string idKey) => array is JsonArray a ? a.Where(x => x.S("name").Length > 0).Select(x => new Label(x.S(idKey, "id"), x.S("name"))).DistinctBy(x => x.Id.Length > 0 ? x.Id : x.Name).ToArray() : [];
    public static IReadOnlyList<ArticleBlock> Blocks(JsonNode? content, int depth = 0)
    {
        if (depth > 6 || content is null) return [];
        if (content is JsonValue) { var raw = content.ToString(); if (raw.TrimStart().StartsWith('[') || raw.TrimStart().StartsWith('{')) { try { return Blocks(JsonNode.Parse(raw), depth + 1); } catch (System.Text.Json.JsonException) { } } return Html(raw); }
        if (content is JsonObject obj)
        {
            if (obj.S("type", "block_type", "content_type").Length > 0) return Blocks(new JsonArray(obj.DeepClone()), depth + 1);
            return Blocks(obj["content"] ?? obj["blocks"] ?? obj["text"], depth + 1);
        }
        if (content is not JsonArray rows) return [];
        var result = new List<ArticleBlock>();
        foreach (var row in rows.Take(512))
        {
            var type = row.S("type", "block_type", "content_type").ToLowerInvariant();
            var text = row.S("text", "html", "content", "value");
            var url = J.Https(row.S("data-original", "original_url", "origin_url", "url", "src", "image_url", "img_url"));
            switch (type)
            {
                case "event_entry": break;
                case "image":
                case "img":
                    url ??= J.Https(row?["data"].S("url", "src"));
                    result.Add(new(url == null ? BlockKind.Notice : BlockKind.Image, url == null ? "图片暂不可用" : "", url, row.S("caption", "desc", "description"))); break;
                case "quote": case "blockquote": result.Add(new(BlockKind.Quote, Children: row?["content"] is JsonArray ? Blocks(row["content"], depth + 1) : Html(text))); break;
                case "game":
                case "game_card":
                    var game = row?["app_info"] ?? row?["game_info"] ?? row?["game"] ?? row?["data"] ?? row; var app = game.I("steam_appid", game.I("appid", game.I("app_id", row.I("gameid"))));
                    result.Add(new(BlockKind.Game, game.S("name", "game_name", "title"), J.Https(game.S("url")), AppId: app > 0 ? app : null)); break;
                case "video":
                    var video = row?["video_info"] ?? row?["video"] ?? row?["data"] ?? row; var direct = J.Https(video.S("video_url", "play_url", "url", "src", "mp4"));
                    result.Add(new(direct == null ? BlockKind.Notice : BlockKind.Video, direct == null ? "视频暂不可用，请打开原文" : video.S("title"), direct, J.Https(video.S("cover", "poster")))); break;
                default: result.AddRange(Blocks(JsonValue.Create(text), depth + 1)); break;
            }
        }
        return result.Take(512).ToArray();
    }
    public static IReadOnlyList<ArticleBlock> Html(string html)
    {
        var doc = new HtmlParser().ParseDocument(html); var blocks = new List<ArticleBlock>();
        if (doc.Body is { } body) foreach (var node in body.ChildNodes) Walk(node, blocks, 0);
        return blocks;
    }
    private static void Walk(INode node, List<ArticleBlock> result, int depth)
    {
        if (depth > 16 || result.Count >= 512) return;
        if (node is IText t) { if (!string.IsNullOrWhiteSpace(t.Data)) result.Add(new(BlockKind.Paragraph, t.Data.Trim())); return; }
        if (node is not IElement el) return;
        var tag = el.TagName.ToLowerInvariant();
        if (tag is "script" or "style" or "noscript") return;
        if (tag == "img") { var url = J.Https(el.GetAttribute("data-original") ?? el.GetAttribute("src") ?? el.GetAttribute("data-src")); if (url != null) result.Add(new(BlockKind.Image, Url: url, Caption: el.GetAttribute("alt"))); return; }
        if (tag == "video") { var url = J.Https(el.GetAttribute("src") ?? el.QuerySelector("source")?.GetAttribute("src")); result.Add(new(url == null ? BlockKind.Notice : BlockKind.Video, "视频", url, el.GetAttribute("poster"))); return; }
        if (tag == "iframe") { result.Add(new(BlockKind.ExternalVideo, "在原网页播放视频", J.Https(el.GetAttribute("src")))); return; }
        if (tag == "blockquote") { var children = new List<ArticleBlock>(); foreach (var child in el.ChildNodes) Walk(child, children, depth + 1); result.Add(new(BlockKind.Quote, Children: children)); return; }
        if (tag is "h1" or "h2" or "h3" or "h4") { result.Add(new(BlockKind.Heading, el.TextContent.Trim(), Level: tag[1] - '0', Runs: Inline(el))); return; }
        if (tag == "li") { result.Add(new(BlockKind.ListItem, el.TextContent.Trim(), Level: depth)); return; }
        if (tag is "p" or "div" && el.QuerySelector("img,video,iframe,p,div,blockquote,ul,ol,h1,h2,h3,h4") == null) { if (!string.IsNullOrWhiteSpace(el.TextContent)) result.Add(new(BlockKind.Paragraph, el.TextContent.Trim(), Runs: Inline(el))); return; }
        foreach (var child in el.ChildNodes) Walk(child, result, depth + 1);
    }
    public static IReadOnlyList<RichRun> Inline(INode node, bool bold = false, bool italic = false, string? url = null, bool code = false, int depth = 0)
    {
        if (depth > 16) return [];
        if (node is IText text) return [new(text.Data, bold, italic, url, code)];
        if (node is not IElement el) return [];
        var tag = el.TagName.ToLowerInvariant(); if (tag == "br") return [new("\n")];
        if (tag is "script" or "style") return [];
        bold |= tag is "b" or "strong"; italic |= tag is "i" or "em"; code |= tag == "code";
        if (tag == "a") url = J.Https(el.GetAttribute("href"));
        return el.ChildNodes.SelectMany(x => Inline(x, bold, italic, url, code, depth + 1)).Take(2048).ToArray();
    }
    public static IReadOnlyList<Comment> Comments(JsonArray? rows, string? root = null)
    {
        var result = new List<Comment>(); if (rows == null) return result;
        foreach (var row in rows.Take(200))
        {
            if (row is not JsonObject) continue;
            if (row["comment"] is JsonArray thread && thread.Count > 0)
            {
                var parsed = Comments(new JsonArray(thread[0]?.DeepClone()), root);
                if (parsed.Count > 0) { var main = parsed[0]; var children = Comments(new JsonArray(thread.Skip(1).Select(x => x?.DeepClone()).ToArray()), main.Id); result.Add(main with { Replies = children }); }
                continue;
            }
            var c = row["comment"] as JsonObject ?? row; var id = c.S("comment_id", "commentid", "id");
            if (id.Length == 0) { if (row["comments"] is JsonArray nested) result.AddRange(Comments(nested, root)); continue; }
            var text = Text(c.S("text", "content", "comment"));
            var replies = Comments(row.Array("sub_comments", "subcomments", "replies", "child_comments"), root ?? id);
            var images = c.Array("imgs", "images")?.Select(x => J.Https(x is JsonValue ? x.ToString() : x.S("url", "src"))).OfType<string>().ToArray() ?? [];
            result.Add(new(id, root ?? (c.S("root_id", "root_comment_id") is { Length: > 0 } r ? r : id), text, Profile(c["user"] ?? c["user_info"]) ?? new(), c.S("create_str", "date"), c.I("comment_award_num", c.I("award_num", c.I("up", c.I("support_num")))), c.S("is_support") is "1" or "true", replies, images));
        }
        return result;
    }
}
