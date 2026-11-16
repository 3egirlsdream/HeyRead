using System.Text.Json;
namespace HeyRead.Core;

public sealed record Preferences
{
    public string Theme { get; set; } = "System";
    public string Accent { get; set; } = "System";
    public string Language { get; set; } = "zh-CN";
    public string CardLayout { get; set; } = "Compact";
    public string Gateway { get; set; } = "https://api.xiaoheihe.cn";
    public bool NoImages { get; set; }
    public bool Animations { get; set; } = true;
    public bool RecordHistory { get; set; } = true;
    public bool RestoreNavigation { get; set; } = true;
    public double BodySize { get; set; } = 17;
    public double LineHeight { get; set; } = 30;
    public double ParagraphSpacing { get; set; } = 20;
    public string BodyFont { get; set; } = "System";
    public string UiFont { get; set; } = "System";
    public bool BlockingEnabled { get; set; } = true;
    public bool BlockFavorites { get; set; }
    public bool BlockHistory { get; set; }
    public List<BlockRule> Rules { get; set; } = [];
    public string LastPage { get; set; } = "discover";
    public Story? LastStory { get; set; }
    public double ListWidth { get; set; } = 370;
}
public sealed record HistoryEntry(Story Story, DateTimeOffset Viewed, double ArticleOffset = 0, double CommentOffset = 0);
public sealed record SearchEntry(string Query, Destination? Scope, DateTimeOffset Time);
public sealed class LibraryStore
{
    public string Root { get; }
    public Preferences Preferences { get; private set; }
    public List<HistoryEntry> History { get; private set; }
    public List<SearchEntry> Searches { get; private set; }
    public Dictionary<string, string> Drafts { get; private set; }
    private readonly object _sync = new();
    public LibraryStore(string root)
    {
        Root = Path.GetFullPath(root); Directory.CreateDirectory(Root); Directory.CreateDirectory(Path.Combine(Root, "cache")); Directory.CreateDirectory(Path.Combine(Root, "fonts"));
        Preferences = Read<Preferences>("preferences.json") ?? new(); History = Read<List<HistoryEntry>>("history.json") ?? []; Searches = Read<List<SearchEntry>>("searches.json") ?? []; Drafts = Read<Dictionary<string, string>>("drafts.json") ?? [];
        Preferences.BodySize = Math.Clamp(Preferences.BodySize, 14, 26); Preferences.LineHeight = Math.Clamp(Preferences.LineHeight, Preferences.BodySize * 1.2, Preferences.BodySize * 2.4); Preferences.ParagraphSpacing = Math.Clamp(Preferences.ParagraphSpacing, 8, 40);
    }
    private T? Read<T>(string name) { try { return JsonSerializer.Deserialize<T>(File.ReadAllBytes(Path.Combine(Root, name)), BlockingCodec.Options); } catch (Exception e) when (e is IOException or JsonException or NotSupportedException) { return default; } }
    public void SavePreferences() => Write("preferences.json", Preferences);
    public void Visit(Story story, double? article = null, double? comment = null)
    {
        lock (_sync) { if (!Preferences.RecordHistory) return; var old = History.FirstOrDefault(x => x.Story.Key == story.Key); History.RemoveAll(x => x.Story.Key == story.Key); History.Insert(0, new(story, DateTimeOffset.UtcNow, article ?? old?.ArticleOffset ?? 0, comment ?? old?.CommentOffset ?? 0)); if (History.Count > 1000) History.RemoveRange(1000, History.Count - 1000); Write("history.json", History); }
    }
    public void ClearHistory() { History.Clear(); Write("history.json", History); }
    public void Search(string query, Destination? scope)
    {
        Searches.RemoveAll(x => x.Query == query && x.Scope == scope); Searches.Insert(0, new(query, scope, DateTimeOffset.UtcNow)); if (Searches.Count > 50) Searches.RemoveRange(50, Searches.Count - 50); Write("searches.json", Searches);
    }
    public void ClearSearches() { Searches.Clear(); Write("searches.json", Searches); }
    public void SetDraft(string key, string text) { if (string.IsNullOrEmpty(text)) Drafts.Remove(key); else Drafts[key] = text; Write("drafts.json", Drafts); }
    public IEnumerable<Story> Filter(IEnumerable<Story> stories, string scope, bool reveal = false) => reveal || !Preferences.BlockingEnabled || scope == "favorites" && !Preferences.BlockFavorites || scope.Contains("history") && !Preferences.BlockHistory ? stories : stories.Where(s => !Preferences.Rules.Any(r => r.Matches(s)));
    public void Merge(BlockImport import)
    {
        var all = Preferences.Rules.Concat(import.Rules).DistinctBy(x => x.Identity).ToList(); if (all.Count > 500) throw new ArgumentException("规则最多 500 条"); Preferences.Rules = all; SavePreferences();
    }
    private void Write<T>(string name, T data)
    {
        lock (_sync) { var path = Path.Combine(Root, name); var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp"; try { File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(data, BlockingCodec.Options)); if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite); File.Move(temp, path, true); } finally { if (File.Exists(temp)) File.Delete(temp); } }
    }
    public long CacheBytes() => Directory.EnumerateFiles(Path.Combine(Root, "cache"), "*", SearchOption.AllDirectories).Sum(x => new FileInfo(x).Length);
    public void ClearCache() { foreach (var file in Directory.EnumerateFiles(Path.Combine(Root, "cache"))) File.Delete(file); }
    public string ImportFont(string path)
    {
        if (!new[] { ".ttf", ".otf", ".ttc" }.Contains(Path.GetExtension(path).ToLowerInvariant()) || new FileInfo(path).Length is 0 or > 33554432) throw new ArgumentException("请选择不超过 32 MiB 的 TTF、OTF 或 TTC 字体");
        var name = Path.GetFileName(path); File.Copy(path, Path.Combine(Root, "fonts", name), true); return name;
    }
}
