using System.Reflection;
using System.Text.Json;
namespace HeyRead.Desktop.Resources;

public sealed class Strings(string language)
{
    private readonly Dictionary<string, string> _values = Read(language);
    public string this[string key] => _values.GetValueOrDefault(key) ?? key;
    private static Dictionary<string, string> Read(string language) { using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"HeyRead.Desktop.Resources.{language}.json") ?? Assembly.GetExecutingAssembly().GetManifestResourceStream("HeyRead.Desktop.Resources.zh-CN.json")!; return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!; }
}
