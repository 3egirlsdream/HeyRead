using System.Security.Cryptography;
using System.Text;
namespace HeyRead.Core;
/// <summary>Port of upstream WebRequestSigner.kt (MIT), preserving path normalization and checksum.</summary>
public static class WebRequestSigner
{
    private const string Alphabet = "AB45STUVWZEFGJ6CH01D237IXYPQRKLMN89";
    public static (long Time, string Nonce, string Key) Create(string path)
    {
        var time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        return (time, nonce, Sign(path, time, nonce));
    }
    public static string Sign(string path, long time, string nonce)
    {
        var normalized = "/" + string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries)) + "/";
        var a = Map((time + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), Alphabet[..^2]);
        var b = Map(normalized, Alphabet); var c = Map(nonce, Alphabet);
        var merged = new StringBuilder();
        for (var i = 0; i < Math.Max(a.Length, Math.Max(b.Length, c.Length)); i++)
        { if (i < a.Length) merged.Append(a[i]); if (i < b.Length) merged.Append(b[i]); if (i < c.Length) merged.Append(c[i]); }
        var h = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(merged.ToString()[..Math.Min(20, merged.Length)])));
        var x = h[^6..].Select(z => (int)z).ToArray();
        static int D(int v) => (v & 128) != 0 ? ((v << 1) ^ 27) & 255 : v << 1;
        static int P(int v) => D(v) ^ v;
        static int S(int v) => P(D(v));
        static int B(int v) => S(P(D(v)));
        static int A(int v) => B(v) ^ S(v) ^ P(v);
        var sum = (A(x[0]) ^ B(x[1]) ^ S(x[2]) ^ P(x[3])) + (P(x[0]) ^ A(x[1]) ^ B(x[2]) ^ S(x[3])) + (S(x[0]) ^ P(x[1]) ^ A(x[2]) ^ B(x[3])) + (B(x[0]) ^ S(x[1]) ^ P(x[2]) ^ A(x[3])) + x[4] + x[5];
        return Map(h[..5], Alphabet[..^4]) + (sum % 100).ToString("D2");
    }
    private static string Map(string value, string alphabet) => new(value.Select(c => alphabet[c % alphabet.Length]).ToArray());
}
