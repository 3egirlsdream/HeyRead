namespace HeyRead.Core;

public sealed class SteamService(HeyReadApi api)
{
    public async Task<IReadOnlyList<Game>> SearchAsync(string query, CancellationToken ct)
    {
        var term = query.Trim(); if (term.Length is 0 or > 120) throw new ArgumentException("请输入游戏名称或 AppID");
        if (term.All(char.IsDigit) || term.StartsWith('-') && term[1..].All(char.IsDigit))
        {
            if (!int.TryParse(term, out var app) || app <= 0) throw new ArgumentException("AppID 必须为正整数");
            var json = await api.PublicJsonAsync($"https://store.steampowered.com/api/appdetails/?appids={app}&l=schinese&cc=CN", ct); var item = json[app.ToString()];
            return item.B("success") == true ? [new(app, (item?["data"]).S("name"), J.Https(item?["data"].S("header_image")))] : [];
        }
        var root = await api.PublicJsonAsync("https://store.steampowered.com/api/storesearch/?" + HeyReadApi.Query(new Dictionary<string, string> { ["term"] = term, ["l"] = "schinese", ["cc"] = "CN" }), ct);
        var rows = root.Array("items") ?? throw new ApiException("Steam 搜索缺少 items");
        return rows.Where(x => x.S("type") == "app" && x.I("id") > 0 && x.S("name").Length > 0).Select(x => new Game(x.I("id"), x.S("name"), J.Https(x.S("tiny_image")))).DistinctBy(x => x.AppId).Take(30).ToArray();
    }
    public static string SteamMarkup(string raw)
    {
        var value = raw.Replace("{STEAM_CLAN_IMAGE}", "https://clan.cloudflare.steamstatic.com/images");
        foreach (var tag in new[] { "h1", "h2", "h3", "b", "i", "u", "p", "list", "olist", "quote" })
        {
            var htmlTag = tag switch { "list" => "ul", "olist" => "ol", "quote" => "blockquote", _ => tag };
            value = value.Replace("[" + tag + "]", "<" + htmlTag + ">", StringComparison.OrdinalIgnoreCase).Replace("[/" + tag + "]", "</" + htmlTag + ">", StringComparison.OrdinalIgnoreCase);
        }
        value = System.Text.RegularExpressions.Regex.Replace(value, @"\[img\](https://[^\[<>]+)\[/img\]", "<img src='$1'>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        value = System.Text.RegularExpressions.Regex.Replace(value, @"\[url=(https://[^\]<>]+)\]([^\[]+)\[/url\]", "<a href='$1'>$2</a>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return value.Replace("[*]", "<li>");
    }
    public async Task<Page<Story>> NewsAsync(Game game, string? cursor, CancellationToken ct)
    {
        var query = $"appid={game.AppId}&count=10&maxlength=0&feeds=steam_community_announcements&l=schinese" + (long.TryParse(cursor, out var date) ? $"&enddate={date}" : "");
        var root = await api.PublicJsonAsync("https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?" + query, ct); var rows = root["appnews"].Array("newsitems") ?? throw new ApiException("Steam 公告响应缺少 newsitems");
        var items = rows.Select(n => { var body = SteamMarkup(n.S("contents")); var text = ContentParser.Text(body); var published = n.L("date"); return new Story($"{game.AppId}:{n.S("gid")}", Source.STEAM, n.S("title"), text[..Math.Min(140, text.Length)], body, game.Name, published > 0 ? DateTimeOffset.FromUnixTimeSeconds(published).ToLocalTime().ToString("yyyy-MM-dd") : "Steam", game.Image ?? $"https://cdn.akamai.steamstatic.com/steam/apps/{game.AppId}/header.jpg", J.Https(n.S("url")), game.AppId, published, Blocks: ContentParser.Html(body)); }).ToArray();
        return new(items, rows.Count >= 10, Cursor: items.Length > 0 ? (items.Min(x => x.PublishedAt ?? long.MaxValue) - 1).ToString() : null);
    }
}
