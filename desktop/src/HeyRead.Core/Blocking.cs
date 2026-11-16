using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
namespace HeyRead.Core;

public enum RuleKind { KEYWORD, AUTHOR, SECTION, TOPIC }
public sealed record BlockRule(RuleKind Kind, string Value, string Label = "", bool Enabled = true, Source? Source = null)
{
    [JsonIgnore] public string Identity => $"{Kind}:{Source}:{(Kind == RuleKind.KEYWORD ? Value.ToLowerInvariant() : Value)}";
    public BlockRule Validate()
    {
        var value = Value.Trim(); var label = Label.Trim();
        if (Kind == RuleKind.KEYWORD) { if (value.Length is 0 or > 100 || value.Contains('\n') || value.Contains('\r') || label.Length > 0 || Source != null) throw new ArgumentException("关键词须为 1–100 字符，不能换行"); }
        else { if (Source != Core.Source.HEYBOX || value.Length == 0 || value.Any(c => c is < '0' or > '9') || value.TrimStart('0').Length == 0 || label.Length > 60) throw new ArgumentException("规则 ID 必须是小黑盒有效正整数，备注不超过 60 字"); value = value.TrimStart('0'); }
        return this with { Value = value, Label = label };
    }
    public bool Matches(Story s) => Enabled && Kind switch
    {
        RuleKind.KEYWORD => s.Title.Contains(Value, StringComparison.OrdinalIgnoreCase) || s.Excerpt.Contains(Value, StringComparison.OrdinalIgnoreCase),
        RuleKind.AUTHOR => s.Source == Core.Source.HEYBOX && s.Profile?.Id.TrimStart('0') == Value,
        RuleKind.SECTION => s.Source == Core.Source.HEYBOX && s.Sections?.Any(x => x.Id.TrimStart('0') == Value) == true,
        RuleKind.TOPIC => s.Source == Core.Source.HEYBOX && s.Topics?.Any(x => x.Id.TrimStart('0') == Value) == true,
        _ => false
    };
}
public sealed record BlockImport(IReadOnlyList<BlockRule> Rules, int Duplicates);
public static class BlockingCodec
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };
    public static byte[] Export(IEnumerable<BlockRule> input)
    {
        var rules = input.Select(x => x.Validate()).ToArray(); if (rules.Length > 500 || rules.DistinctBy(x => x.Identity).Count() != rules.Length) throw new ArgumentException("规则最多 500 条且不能重复");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { format = "heyread-content-blocking", version = 1, rules }, Options); if (bytes.Length > 262144) throw new ArgumentException("规则文件超过 256 KiB"); return bytes;
    }
    public static BlockImport Import(byte[] bytes)
    {
        if (bytes.Length > 262144) throw new ArgumentException("规则文件超过 256 KiB");
        var text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        using var doc = JsonDocument.Parse(text, new() { MaxDepth = 16, AllowTrailingCommas = false });
        Check(doc.RootElement, ["format", "version", "rules"]);
        if (doc.RootElement.GetProperty("format").GetString() != "heyread-content-blocking" || doc.RootElement.GetProperty("version").GetInt32() != 1) throw new ArgumentException("不支持的屏蔽规则格式或版本");
        var rows = doc.RootElement.GetProperty("rules"); if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > 500) throw new ArgumentException("规则最多 500 条");
        var rules = new List<BlockRule>(); var seen = new HashSet<string>(); var duplicate = 0;
        foreach (var row in rows.EnumerateArray())
        {
            Check(row, ["kind", "value", "label", "enabled", "source"]);
            if (!Enum.TryParse<RuleKind>(row.GetProperty("kind").GetString(), false, out var kind) || !Enum.IsDefined(kind)) throw new ArgumentException("规则类型无效");
            var sourceJson = row.GetProperty("source"); Source? source = null;
            if (sourceJson.ValueKind != JsonValueKind.Null) { if (!Enum.TryParse<Source>(sourceJson.GetString(), false, out var src) || !Enum.IsDefined(src)) throw new ArgumentException("规则来源无效"); source = src; }
            var rule = new BlockRule(kind, row.GetProperty("value").GetString() ?? "", row.GetProperty("label").GetString() ?? "", row.GetProperty("enabled").GetBoolean(), source).Validate();
            if (seen.Add(rule.Identity)) rules.Add(rule); else duplicate++;
        }
        return new(rules, duplicate);
    }
    private static void Check(JsonElement e, string[] names)
    {
        if (e.ValueKind != JsonValueKind.Object) throw new ArgumentException("规则格式无效"); var seen = new HashSet<string>();
        foreach (var p in e.EnumerateObject()) if (!names.Contains(p.Name) || !seen.Add(p.Name)) throw new ArgumentException("存在未知或重复字段");
        if (seen.Count != names.Length) throw new ArgumentException("规则缺少必要字段");
    }
}
