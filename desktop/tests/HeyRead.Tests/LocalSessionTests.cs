using System.Security.Cryptography;
using System.Text;
using HeyRead.Core;
using Xunit;
namespace HeyRead.Tests;
public sealed class LocalSessionTests
{
    [Fact]
    public void SessionRestoresWithoutPlaintextAndRejectsTampering()
    {
        var root = Temp();
        try
        {
            var store = new LocalSessionStore(root); var session = new Session("pkey=private-test-cookie", "42", "Test", ""); store.Save(session);
            Assert.Equal(session, new LocalSessionStore(root).Load());
            var directory = Path.Combine(root, "account"); var path = Path.Combine(directory, "session.bin"); var encrypted = File.ReadAllBytes(path);
            Assert.DoesNotContain("private-test-cookie", Encoding.UTF8.GetString(encrypted)); Assert.Equal(32, new FileInfo(Path.Combine(directory, "session.key")).Length);
            if (!OperatingSystem.IsWindows()) { Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path)); Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(directory)); }
            encrypted[^1] ^= 1; File.WriteAllBytes(path, encrypted); Assert.Null(store.Load()); store.Save(session); Assert.Equal(session, store.Load());
            store.Save(null); Assert.Null(store.Load()); Assert.Empty(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void IsolatedRootsDoNotShareSessionsOrKeys()
    {
        var first = Temp(); var second = Temp();
        try
        {
            var a = new LocalSessionStore(first); var b = new LocalSessionStore(second); a.Save(new("pkey=a", "1", "A", "")); Assert.Null(b.Load()); b.Save(new("pkey=b", "2", "B", ""));
            Assert.Equal("1", a.Load()?.UserId); Assert.Equal("2", b.Load()?.UserId);
            Assert.NotEqual(File.ReadAllBytes(Path.Combine(first, "account", "session.key")), File.ReadAllBytes(Path.Combine(second, "account", "session.key")));
            Assert.Throws<ArgumentException>(() => a.Save(new("", "1", "", "")));
        }
        finally { Directory.Delete(first, true); Directory.Delete(second, true); }
    }
    private static string Temp() => Path.Combine(Path.GetTempPath(), "heyread-session-" + Guid.NewGuid());
}
