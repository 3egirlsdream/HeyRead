using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using HeyRead.Core;
using HeyRead.Core.Updates;
using HeyRead.Desktop.Updates;
using static HeyRead.Desktop.Controls.Ui;
namespace HeyRead.Desktop.Views;

public sealed partial class MainWindow
{
    private readonly GitHubUpdateService _updates = new();
    private readonly CancellationTokenSource _updateLifetime = new();
    private bool _checkingUpdates;
    private Window? _updateDialog;
    private TextBlock? _updateStatusView;
    private string _lastUpdateStatus = "";
    private async Task CheckAfterStartup()
    {
        if (_vm.VisualFixture || !OperatingSystem.IsMacOS()) return;
        try { await Task.Delay(TimeSpan.FromSeconds(4), _updateLifetime.Token); await CheckForUpdates(false); }
        catch (OperationCanceledException) { }
    }
    private void SetUpdateStatus(string value) { _lastUpdateStatus = value; if (_updateStatusView != null) _updateStatusView.Text = value; }
    private async Task CheckForUpdates(bool manual)
    {
        if (_checkingUpdates) return;
        if (_updateDialog is { IsVisible: true }) { _updateDialog.Activate(); return; }
        _checkingUpdates = true; SetUpdateStatus(T("正在检查更新…"));
        try
        {
            var update = await _updates.CheckAsync(ProductInfo.Version, ct: _updateLifetime.Token);
            if (update == null) { SetUpdateStatus(T("已是最新版本")); if (manual) await Notice(T("已是最新版本") + " · " + ProductInfo.Version); return; }
            SetUpdateStatus(F("发现新版本 {0}", update.Version)); ShowUpdateDialog(update);
        }
        catch (OperationCanceledException) when (_updateLifetime.IsCancellationRequested) { }
        catch (Exception e) { SetUpdateStatus(T("检查更新失败") + " · " + T(e.Message)); if (manual && IsVisible) await Notice(T("检查更新失败") + "\n" + T(e.Message)); }
        finally { _checkingUpdates = false; }
    }
    private Control BuildUpdateSettings()
    {
        _updateStatusView = Text(_lastUpdateStatus, 12, muted: true);
        return Group(Text(T("软件更新"), 17, true), Text(T("当前版本") + " · " + ProductInfo.Version), AsyncButton(T("检查更新"), () => CheckForUpdates(true)), _updateStatusView);
    }
    private void ShowUpdateDialog(AvailableUpdate update)
    {
        var window = Dialog(T("软件更新"), 540, 440); _updateDialog = window;
        var state = Text(F("发现新版本 {0}", update.Version), 14, true);
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 5, IsVisible = false };
        var notes = new ScrollViewer { Content = Text(update.Notes, 13), MaxHeight = 180 };
        var later = Button(T("稍后"), window.Close); Button? install = null; var working = false; var handoff = false;
        // The linked token is owned by the modeless window, not by this short-lived construction method.
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_updateLifetime.Token);
        var cancel = Button(T("取消下载"), () => lifetime.Cancel()); cancel.IsVisible = false;
        install = AsyncButton(T("自动更新"), async () =>
        {
            if (working) return; working = true; later.IsEnabled = false; cancel.IsVisible = true; progress.IsVisible = true; notes.IsVisible = false;
            try
            {
                var reporter = new Progress<UpdateProgress>(p => { progress.Value = p.Percentage; state.Text = T(p.State); });
                var prepared = await new MacUpdateInstaller(_updates, _vm.Store.Root).PrepareAsync(update, reporter, lifetime.Token);
                lifetime.Token.ThrowIfCancellationRequested(); cancel.IsEnabled = false; state.Text = T("正在重启以完成更新…");
                SaveOffsets(); _vm.Store.SavePreferences(); MacUpdateInstaller.StartHandoff(prepared); handoff = true;
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime app) app.Shutdown();
            }
            catch (OperationCanceledException) { state.Text = T("下载已取消"); }
            catch (Exception e) { state.Text = T("更新失败，当前版本已保留") + "\n" + T(e.Message); }
            finally { working = false; if (!handoff) { later.IsEnabled = true; cancel.IsVisible = false; install!.Content = T("重试更新"); if (lifetime.IsCancellationRequested) window.Close(); } }
        }, "primary");
        window.Closing += (_, e) => { if (working && !handoff) { lifetime.Cancel(); e.Cancel = true; } };
        window.Closed += (_, _) => { lifetime.Cancel(); lifetime.Dispose(); _updateDialog = null; };
        window.Content = Stack(18, Text(ProductInfo.DisplayName, 20, true), state, Text(ProductInfo.Version + " → " + update.Version + $" · {update.Asset.Size / 1048576d:F1} MB", 13, muted: true), notes, progress, Row(8, later, cancel, install)); ((Control)window.Content).Margin = new(24);
        window.Show(this);
    }
}
