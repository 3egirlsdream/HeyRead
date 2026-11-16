using Avalonia;
using Avalonia.Controls;
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
            var defaultRoot = OperatingSystem.IsMacOS() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "HeyRead") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HeyRead");
            var store = new LibraryStore(testRoot ?? defaultRoot);
            _api = new() { Gateway = () => store.Preferences.Gateway, VerifyCaptcha = Native.VerifyCaptchaAsync };
            var sessions = new LocalSessionStore(store.Root); _api.SetSession(sessions.Load());
            var vm = new MainViewModel(_api, new SteamService(_api), store, sessions, new AppleTranslation());
            lifetime.MainWindow = new MainWindow(vm);
            if (OperatingSystem.IsMacOS())
            {
                lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                void Reopen()
                {
                    if (lifetime.Windows.Count == 0) { lifetime.MainWindow = new MainWindow(vm); lifetime.MainWindow.Show(); }
                    lifetime.MainWindow?.Activate();
                }
                if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
                    activatable.Activated += (_, e) => { if (e.Kind == ActivationKind.Reopen) Reopen(); };
                var menu = new NativeMenu(); var reopen = new NativeMenuItem { Header = "盒阅 · HeyRead" }; reopen.Click += (_, _) => Reopen(); menu.Items.Add(reopen);
                NativeMenu.SetMenu(this, menu);
            }
            lifetime.Exit += (_, _) => { vm.Dispose(); _api.Dispose(); };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
