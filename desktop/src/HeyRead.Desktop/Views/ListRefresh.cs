using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using HeyRead.Desktop.Controls;
using static HeyRead.Desktop.Controls.Ui;

namespace HeyRead.Desktop.Views;

public sealed partial class MainWindow
{
    private const double RefreshThreshold = 2.4;
    private readonly DispatcherTimer _pullTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly TranslateTransform _listPull = new();
    private readonly Border _pullHint = new() { Height = 30, VerticalAlignment = VerticalAlignment.Top, IsVisible = false, IsHitTestVisible = false };
    private readonly TextBlock _pullLabel = Text("", 12, muted: true);
    private double _pullDistance;
    private bool _pullStartedAwayFromTop;

    private Grid BuildListViewport()
    {
        var prompt = Row(5, Icons.Get("arrow_clockwise", 13, "Muted"), _pullLabel);
        prompt.HorizontalAlignment = HorizontalAlignment.Center;
        _pullHint.Child = prompt;
        _list.RenderTransform = _listPull;
        _pullTimer.Tick += (_, _) => FinishListPull();
        var viewport = new Grid { ClipToBounds = true, Children = { _pullHint, _list, _listLoading } };
        viewport.AddHandler(PointerWheelChangedEvent, OnListWheel, RoutingStrategies.Tunnel, handledEventsToo: true);
        return viewport;
    }

    private void OnListWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || Math.Abs(e.Delta.Y) <= Math.Abs(e.Delta.X)) return;
        if (_listScroll is not { IsEffectivelyVisible: true } scroll || _vm.Loading) return;

        if (e.Delta.Y < 0)
        {
            _pagesWithoutVisibleRows = 0;
            QueuePagination();
        }

        if (!_pullTimer.IsEnabled)
            _pullStartedAwayFromTop = scroll.Offset.Y > .5;
        if (e.Delta.Y < 0 || scroll.Offset.Y > .5)
        {
            _pullStartedAwayFromTop = true;
            _pullDistance = 0;
            ShowPullProgress();
        }
        else if (!_pullStartedAwayFromTop)
        {
            _pullDistance = Math.Min(RefreshThreshold, _pullDistance + Math.Min(e.Delta.Y, 1.2));
            ShowPullProgress();
        }
        _pullTimer.Stop();
        _pullTimer.Start();
    }

    private void ShowPullProgress()
    {
        _pullHint.IsVisible = _pullDistance > 0;
        _listPull.Y = _pullDistance / RefreshThreshold * _pullHint.Height;
        _pullLabel.Text = T(_pullDistance >= RefreshThreshold ? "松开刷新" : "下拉刷新");
    }

    private void FinishListPull()
    {
        var refresh = _pullDistance >= RefreshThreshold && !_vm.Loading && _listScroll is { IsEffectivelyVisible: true };
        ResetListPull();
        if (refresh) Run(() => _vm.LoadAsync());
        else QueuePagination();
    }

    private void ResetListPull()
    {
        _pullTimer.Stop();
        _pullDistance = 0;
        _pullStartedAwayFromTop = false;
        _listPull.Y = 0;
        _pullHint.IsVisible = false;
    }
}
