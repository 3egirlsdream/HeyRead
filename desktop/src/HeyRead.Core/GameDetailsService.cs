namespace HeyRead.Core;
public sealed class GameDetailsService(HeyReadApi api)
{
    private readonly Dictionary<int, (GameDetails Details, DateTimeOffset Loaded)> _cache = [];
    private readonly object _lock = new();
    public async Task<GameDetails> LoadAsync(int appId, CancellationToken ct)
    {
        if (appId <= 0) throw new ArgumentException("AppID 必须为正整数");
        lock (_lock) if (_cache.TryGetValue(appId, out var cached) && DateTimeOffset.UtcNow - cached.Loaded < TimeSpan.FromHours(6)) return cached.Details;
        var json = await api.PublicJsonAsync($"https://store.steampowered.com/api/appdetails/?appids={appId}&l=schinese&cc=CN", ct).ConfigureAwait(false);
        var app = json[appId.ToString()]; if (app.B("success") != true) throw new ApiException("游戏资料暂不可用");
        var data = app?["data"]; var name = data.TextValue("name"); if (name.Length == 0) throw new ApiException("游戏资料未返回名称");
        var details = new GameDetails(appId, name, data.FirstHttps("header_image"), string.Join(" / ", data.Array("genres")?.Select(g => g.S("description")).Where(x => x.Length > 0).Take(2) ?? []), data.Array("developers")?.FirstOrDefault()?.ToString() ?? "", (data?["price_overview"]).S("final_formatted"));
        ct.ThrowIfCancellationRequested();
        lock (_lock) { _cache[appId] = (details, DateTimeOffset.UtcNow); while (_cache.Count > 64) _cache.Remove(_cache.MinBy(x => x.Value.Loaded).Key); }
        return details;
    }
}
