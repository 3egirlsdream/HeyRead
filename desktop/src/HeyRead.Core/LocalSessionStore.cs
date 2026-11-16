using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace HeyRead.Core;

/// <summary>Authenticated local encryption. Both files are private to the current filesystem user; no OS credential service is used.</summary>
public sealed class LocalSessionStore : ISessionStore
{
    private readonly string _directory;
    private readonly object _sync = new();
    private static readonly byte[] Context = "HeyRead local session v1"u8.ToArray();
    public LocalSessionStore(string root)
    {
        _directory = Path.Combine(Path.GetFullPath(root), "account"); Directory.CreateDirectory(_directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    private string SessionPath => Path.Combine(_directory, "session.bin");
    private string KeyPath => Path.Combine(_directory, "session.key");
    public Session? Load()
    {
        lock (_sync)
        {
            if (!File.Exists(SessionPath) || !File.Exists(KeyPath)) return null;
            byte[]? plaintext = null; byte[]? key = null;
            try
            {
                if (new FileInfo(SessionPath).Length is < 29 or > 65536 || new FileInfo(KeyPath).Length != 32) return null;
                key = File.ReadAllBytes(KeyPath); var bytes = File.ReadAllBytes(SessionPath); if (bytes[0] != 1) return null;
                plaintext = new byte[bytes.Length - 29]; using var aes = new AesGcm(key, 16); aes.Decrypt(bytes.AsSpan(1, 12), bytes.AsSpan(29), bytes.AsSpan(13, 16), plaintext, Context);
                var session = JsonSerializer.Deserialize<Session>(plaintext); return session?.IsValid == true ? session : null;
            }
            catch (Exception e) when (e is CryptographicException or JsonException or IOException or UnauthorizedAccessException) { return null; }
            finally { if (plaintext != null) CryptographicOperations.ZeroMemory(plaintext); if (key != null) CryptographicOperations.ZeroMemory(key); }
        }
    }
    public void Save(Session? session)
    {
        lock (_sync)
        {
            if (session == null) { File.Delete(SessionPath); File.Delete(KeyPath); return; }
            if (!session.IsValid) throw new ArgumentException("登录会话无效");
            var key = ReadOrCreateKey(); var plaintext = JsonSerializer.SerializeToUtf8Bytes(session);
            try
            {
                if (plaintext.Length > 65507) throw new ArgumentException("登录会话数据过大");
                var encrypted = new byte[29 + plaintext.Length]; encrypted[0] = 1; RandomNumberGenerator.Fill(encrypted.AsSpan(1, 12));
                using var aes = new AesGcm(key, 16); aes.Encrypt(encrypted.AsSpan(1, 12), plaintext, encrypted.AsSpan(29), encrypted.AsSpan(13, 16), Context); WritePrivate(SessionPath, encrypted, true);
            }
            finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plaintext); }
        }
    }
    private byte[] ReadOrCreateKey()
    {
        if (File.Exists(KeyPath)) { var stored = File.ReadAllBytes(KeyPath); if (stored.Length == 32) return stored; CryptographicOperations.ZeroMemory(stored); File.Delete(KeyPath); }
        var key = RandomNumberGenerator.GetBytes(32);
        try { WritePrivate(KeyPath, key, false); return key; }
        catch (IOException) when (File.Exists(KeyPath)) { CryptographicOperations.ZeroMemory(key); var stored = File.ReadAllBytes(KeyPath); if (stored.Length != 32) throw new IOException("本机登录密钥无效"); return stored; }
    }
    private static void WritePrivate(string path, byte[] data, bool overwrite)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var file = new FileStream(temporary, options)) { file.Write(data); file.Flush(true); }
            File.Move(temporary, path, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
