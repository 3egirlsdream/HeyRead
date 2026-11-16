using System.Text.Json;
namespace HeyRead.Core;

public sealed record CaptchaChallenge(string Path, string AppId);
public sealed class CaptchaProof
{
    private readonly string _ticket, _randstr; private int _consumed;
    private CaptchaProof(string ticket, string randstr) { _ticket = ticket; _randstr = randstr; }
    public static CaptchaProof Parse(string json)
    {
        if (json.Length > 16384) throw new ApiException("验证码返回无效结果");
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        if (!root.TryGetProperty("ret", out var ret) || !ret.TryGetInt32(out var value) || value != 0) throw new ApiException("验证未完成，请重试", "show_captcha");
        if (root.TryGetProperty("errorCode", out var error) && error.ValueKind != JsonValueKind.Null && (!error.TryGetInt32(out var code) || code != 0)) throw new ApiException("验证返回错误凭据");
        var ticket = root.GetProperty("ticket").GetString() ?? ""; var randstr = root.GetProperty("randstr").GetString() ?? "";
        if (ticket.Length is 0 or > 4096 || randstr.Length is 0 or > 512 || ticket.StartsWith("trerror_", StringComparison.OrdinalIgnoreCase) || ticket.Any(char.IsControl) || randstr.Any(char.IsControl)) throw new ApiException("验证票据无效，请重试");
        return new(ticket, randstr);
    }
    public void ApplyOnce(Dictionary<string, string> parameters) { if (Interlocked.Exchange(ref _consumed, 1) != 0) throw new InvalidOperationException("验证票据已使用"); parameters["captcha_type"] = "confirm"; parameters["ticket"] = _ticket; parameters["randstr"] = _randstr; }
    public override string ToString() => "CaptchaProof(<redacted>)";
}
