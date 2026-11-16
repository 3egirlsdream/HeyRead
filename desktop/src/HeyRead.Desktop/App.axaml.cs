using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HeyRead.Core;
using HeyRead.Desktop.Platform;
using HeyRead.Desktop.ViewModels;
using HeyRead.Desktop.Views;
namespace HeyRead.Desktop;

public sealed partial class App : Application
{
    private HeyReadApi? _api;
    public override void Initialize() { Name = "HeyRead"; AvaloniaXamlLoader.Load(this); }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            var testRoot = Environment.GetEnvironmentVariable("HEYREAD_TEST_ROOT");
            var store = new LibraryStore(testRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HeyRead"));
            _api = new() { Gateway = () => store.Preferences.Gateway, VerifyCaptcha = Native.VerifyCaptchaAsync };
            var sessions = new MacSessionStore(testRoot != null, testRoot); _api.SetSession(sessions.Load());
            var vm = new MainViewModel(_api, new SteamService(_api), store, sessions, new AppleTranslation());
            lifetime.MainWindow = new MainWindow(vm);
            lifetime.Exit += (_, _) => { vm.Dispose(); _api.Dispose(); };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
