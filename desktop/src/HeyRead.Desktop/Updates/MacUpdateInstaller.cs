using System.Diagnostics;
using System.Text.Json;
using HeyRead.Core;
using HeyRead.Core.Updates;
namespace HeyRead.Desktop.Updates;

public sealed record InstallPlan(string Id, string Target, string Staged, string Backup, string Version, int ParentPid, string DataRoot, bool Offline, string Status = "prepared", string? Error = null, int? NewPid = null);
public sealed record PreparedUpdate(string Directory, string Receipt, InstallPlan Plan);
public sealed class MacUpdateInstaller(GitHubUpdateService source, string dataRoot)
{
    public static string CurrentBundle()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null) { if (directory.Name.EndsWith(".app", StringComparison.Ordinal) && File.Exists(Path.Combine(directory.FullName, "Contents", "Info.plist"))) return directory.FullName; directory = directory.Parent; }
        throw new InvalidOperationException("请从已安装的 .app 启动后再自动更新");
    }
    public async Task<PreparedUpdate> PrepareAsync(AvailableUpdate update, IProgress<UpdateProgress>? progress, CancellationToken ct)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        if (GitHubUpdateService.ParseVersion(update.Version) <= GitHubUpdateService.ParseVersion(ProductInfo.Version)) throw new InvalidOperationException("更新版本必须高于当前版本");
        var target = CurrentBundle(); if (new DirectoryInfo(target).LinkTarget != null) throw new IOException("请从实际安装位置启动应用后再更新");
        var parent = Path.GetDirectoryName(target)!; var test = Path.Combine(parent, ".xiaoheihe-write-" + Guid.NewGuid().ToString("N"));
        try { using (File.Create(test)) { } File.Delete(test); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new IOException("应用所在位置不可写，请先将应用移到应用程序文件夹", e); }
        var id = Guid.NewGuid().ToString("N"); var context = Path.Combine(dataRoot, "updates", id); Directory.CreateDirectory(context); File.SetUnixFileMode(context, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var staged = Path.Combine(parent, "." + ProductInfo.PackageName + "-" + id + ".stage.app");
        try
        {
            var archive = await source.DownloadAsync(update, context, progress, ct).ConfigureAwait(false); progress?.Report(new(100, "正在校验更新…"));
            UpdateArchive.Validate(archive); var extracted = Path.Combine(context, "extracted"); Directory.CreateDirectory(extracted);
            await RunAsync("/usr/bin/ditto", ["-x", "-k", archive, extracted], ct).ConfigureAwait(false);
            var app = Path.Combine(extracted, ProductInfo.PackageName + ".app"); await ValidateBundle(app, update.Version, ct).ConfigureAwait(false);
            progress?.Report(new(100, "正在准备安装…")); await RunAsync("/usr/bin/ditto", [app, staged], ct).ConfigureAwait(false); await ValidateBundle(staged, update.Version, ct).ConfigureAwait(false);
            var plan = new InstallPlan(id, target, staged, Path.Combine(parent, "." + ProductInfo.PackageName + "-" + id + ".backup.app"), update.Version, Environment.ProcessId, dataRoot, Environment.GetEnvironmentVariable("HEYREAD_DEMO") == "1");
            var receipt = Path.Combine(context, "receipt.json"); SavePlan(receipt, plan);
            File.WriteAllText(Path.Combine(dataRoot, "updates", "last-receipt.txt"), receipt);
            return new(context, receipt, plan);
        }
        catch { if (Directory.Exists(staged)) Directory.Delete(staged, true); if (Directory.Exists(context)) Directory.Delete(context, true); throw; }
    }
    private static async Task ValidateBundle(string app, string version, CancellationToken ct)
    {
        var info = Path.Combine(app, "Contents", "Info.plist");
        async Task<string> Field(string name) => (await RunAsync("/usr/libexec/PlistBuddy", ["-c", "Print :" + name, info], ct).ConfigureAwait(false)).Trim();
        if (await Field("CFBundleIdentifier") != ProductInfo.BundleId || await Field("CFBundleExecutable") != ProductInfo.PackageName || await Field("CFBundleShortVersionString") != version) throw new InvalidDataException("更新应用的名称、标识或版本不匹配");
        var arch = GitHubUpdateService.CurrentRid == "osx-arm64" ? "arm64" : "x86_64";
        await RunAsync("/usr/bin/lipo", [Path.Combine(app, "Contents", "MacOS", ProductInfo.PackageName), "-verify_arch", arch], ct).ConfigureAwait(false);
        await RunAsync("/usr/bin/lipo", [Path.Combine(app, "Contents", "MacOS", "libHeyReadNative.dylib"), "-verify_arch", arch], ct).ConfigureAwait(false);
        await RunAsync("/usr/bin/codesign", ["--verify", "--deep", "--strict", app], ct).ConfigureAwait(false);
    }
    public static void StartHandoff(PreparedUpdate prepared)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        var plan = prepared.Plan;
        var scriptPath = Path.Combine(prepared.Directory, "install.sh");
        File.WriteAllText(scriptPath, BuildScript(prepared)); File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var process = Process.Start(new ProcessStartInfo("/bin/bash") { UseShellExecute = false, ArgumentList = { scriptPath } }) ?? throw new IOException("无法启动更新安装程序"); process.Dispose();
    }
    public static string ShellQuote(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";
    public static string BuildScript(PreparedUpdate update)
    {
        var p = update.Plan;
        return $$"""
#!/bin/bash
set -u
CURRENT={{ShellQuote(p.Target)}}
STAGED={{ShellQuote(p.Staged)}}
BACKUP={{ShellQuote(p.Backup)}}
CONTEXT={{ShellQuote(update.Directory)}}
RECEIPT={{ShellQuote(update.Receipt)}}
TOKEN={{ShellQuote(p.Id)}}
ROOT={{ShellQuote(p.DataRoot)}}
PARENT_PID={{p.ParentPid}}
exec >> "$CONTEXT/install.log" 2>&1
status() { /usr/bin/plutil -replace Status -string "$1" "$RECEIPT"; }
launch_old() { /usr/bin/open -n "$CURRENT" --args --data-root "$ROOT" {{(p.Offline ? "--offline-demo" : "")}}; }
rollback() {
    new_pid=$(/usr/bin/plutil -extract NewPid raw -o - "$RECEIPT" 2>/dev/null || true)
    if [[ "$new_pid" =~ ^[1-9][0-9]*$ ]]; then /bin/kill -TERM "$new_pid" 2>/dev/null || true; fi
    status failed
    if [ -d "$BACKUP" ]; then
        if [ -d "$CURRENT" ]; then /bin/mv "$CURRENT" "$CONTEXT/failed.app"; fi
        /bin/mv "$BACKUP" "$CURRENT" || exit 1
    fi
    launch_old
    exit 1
}
for ((i=0;i<120;i++)); do
    if ! kill -0 "$PARENT_PID" 2>/dev/null; then break; fi
    /bin/sleep 0.5
done
if kill -0 "$PARENT_PID" 2>/dev/null; then status failed; exit 1; fi
if [ ! -d "$STAGED" ] || [ -e "$BACKUP" ]; then status failed; launch_old; exit 1; fi
status installing
/bin/mv "$CURRENT" "$BACKUP" || { status failed; launch_old; exit 1; }
/bin/mv "$STAGED" "$CURRENT" || rollback
/usr/bin/open -n "$CURRENT" --args --update-receipt "$RECEIPT" --update-token "$TOKEN" --data-root "$ROOT" {{(p.Offline ? "--offline-demo" : "")}} || rollback
for ((i=0;i<120;i++)); do
    result=$(/usr/bin/plutil -extract Status raw -o - "$RECEIPT" 2>/dev/null || true)
    if [ "$result" = ready ]; then
        status completed
        /bin/rm -rf "$BACKUP" "$CONTEXT/extracted"
        /bin/rm -f "$CONTEXT/"*.zip
        exit 0
    fi
    /bin/sleep 0.5
done
rollback
""";
    }
    public static void RecordLaunching(string receipt, string token)
    {
        try { var plan = JsonSerializer.Deserialize<InstallPlan>(File.ReadAllText(receipt)); if (plan?.Id == token && plan.Status == "installing" && plan.Target == CurrentBundle() && plan.Version == ProductInfo.Version) SavePlan(receipt, plan with { NewPid = Environment.ProcessId }); }
        catch (Exception e) when (e is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException) { }
    }
    public static bool ConfirmStartup(string receipt, string token)
    {
        try
        {
            var plan = JsonSerializer.Deserialize<InstallPlan>(File.ReadAllText(receipt));
            if (plan == null || plan.Id != token || plan.Version != ProductInfo.Version || Path.GetFullPath(plan.Target) != CurrentBundle() || plan.Status != "installing") return false;
            SavePlan(receipt, plan with { Status = "ready", NewPid = Environment.ProcessId }); return true;
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException) { return false; }
    }
    private static void SavePlan(string path, InstallPlan plan)
    {
        var temporary = path + ".tmp"; File.WriteAllText(temporary, JsonSerializer.Serialize(plan)); if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite); File.Move(temporary, path, true);
    }
    private static async Task<string> RunAsync(string file, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var start = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("无法启动更新校验工具");
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(ct); var error = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct).ConfigureAwait(false); var text = await output.ConfigureAwait(false); var message = await error.ConfigureAwait(false);
            if (process.ExitCode != 0) throw new IOException("更新校验或文件操作失败：" + message); return text;
        }
        finally { if (!process.HasExited) process.Kill(true); }
    }
}
