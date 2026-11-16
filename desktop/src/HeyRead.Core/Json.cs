using System.Text.Json.Nodes;
namespace HeyRead.Core;

public static class J
{
    public static string S(this JsonNode? n, params string[] keys)
    {
        foreach (var key in keys) { var v = n is JsonObject o ? o[key] : null; if (v is JsonValue) { var s = v.ToString().Trim(); if (s.Length > 0 && s is not "null" and not "undefined") return s; } }
        return "";
    }
    public static string TextValue(this JsonNode? n, params string[] keys) { foreach (var key in keys) if (n is JsonObject o && o[key] is JsonValue v && v.TryGetValue<string>(out var value) && !string.IsNullOrWhiteSpace(value) && value.Trim() is not "null" and not "undefined") return value.Trim(); return ""; }
    public static string? FirstHttps(this JsonNode? n, params string[] keys) => keys.Select(k => Https(n.S(k))).FirstOrDefault(x => x != null);
    public static int I(this JsonNode? n, string key, int fallback = 0) => int.TryParse(n.S(key), out var i) ? i : fallback;
    public static long L(this JsonNode? n, string key) => long.TryParse(n.S(key), out var i) ? i : 0;
    public static bool? B(this JsonNode? n, string key) => n.S(key).ToLowerInvariant() switch { "true" or "1" or "yes" => true, "false" or "0" or "no" => false, _ => null };
    public static JsonArray? Array(this JsonNode? n, params string[] keys) => Find(n, keys, 0);
    private static JsonArray? Find(JsonNode? n, string[] keys, int depth)
    {
        if (depth > 5 || n is not JsonObject o) return null;
        foreach (var key in keys) if (o[key] is JsonArray a) return a;
        foreach (var key in new[] { "data", "result", "list_data", "favorite_data", "history_data", "folder", "folder_info", "item" }) if (Find(o[key], keys, depth + 1) is { } a) return a;
        return null;
    }
    public static string? Https(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo) ? uri.AbsoluteUri : null;
}
