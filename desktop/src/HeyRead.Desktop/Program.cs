using Avalonia;
namespace HeyRead.Desktop;

internal static class Program
{
    [STAThread] public static void Main(string[] args) { if (args.Contains("--version")) { Console.WriteLine("HeyRead 0.2.0 · Avalonia 12.1.6 · .NET 10"); return; } if (args.Contains("--probe-account")) { Probe.AccountAsync().GetAwaiter().GetResult(); return; } if (args.Contains("--probe")) { Probe.RunAsync().GetAwaiter().GetResult(); return; } BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().With(new MacOSPlatformOptions { ShowInDock = true }).LogToTrace();
}
