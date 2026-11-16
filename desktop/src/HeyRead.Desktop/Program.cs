using Avalonia;
namespace HeyRead.Desktop;

internal static class Program
{
    [STAThread] public static void Main(string[] args) { if (args.Contains("--probe")) { Probe.RunAsync().GetAwaiter().GetResult(); return; } BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().With(new MacOSPlatformOptions { ShowInDock = true }).LogToTrace();
}
