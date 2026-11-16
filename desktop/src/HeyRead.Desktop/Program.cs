using Avalonia;
using HeyRead.Core;
using HeyRead.Core.Updates;
using HeyRead.Desktop.Updates;
namespace HeyRead.Desktop;

internal static class Program
{
    internal static string? Receipt { get; private set; }
    internal static string? UpdateToken { get; private set; }
    [STAThread] public static void Main(string[] args)
    {
        string? Value(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        if (Value("--data-root") is { } root) { if (!Path.IsPathFullyQualified(root)) throw new ArgumentException("数据目录必须是绝对路径"); Environment.SetEnvironmentVariable("HEYREAD_TEST_ROOT", root); }
        if (args.Contains("--offline-demo")) Environment.SetEnvironmentVariable("HEYREAD_DEMO", "1");
        Receipt = Value("--update-receipt"); UpdateToken = Value("--update-token");
        if (Receipt != null && UpdateToken != null) MacUpdateInstaller.RecordLaunching(Receipt, UpdateToken);
        if (args.Contains("--version")) { Console.WriteLine(ProductInfo.DisplayName + " " + ProductInfo.Version); return; }
        if (args.Contains("--check-updates")) { CheckUpdates().GetAwaiter().GetResult(); return; }
        if (args.Contains("--probe-account")) { Probe.AccountAsync().GetAwaiter().GetResult(); return; }
        if (args.Contains("--probe")) { Probe.RunAsync().GetAwaiter().GetResult(); return; }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    private static async Task CheckUpdates()
    {
        using var updater = new GitHubUpdateService(); var update = await updater.CheckAsync(ProductInfo.Version);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { currentVersion = ProductInfo.Version, available = update != null, latestVersion = update?.Version, asset = update?.Asset.File }));
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().With(new MacOSPlatformOptions { ShowInDock = true }).LogToTrace();
}
