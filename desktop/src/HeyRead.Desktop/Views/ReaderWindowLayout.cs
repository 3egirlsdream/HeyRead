using Avalonia;
using Avalonia.Controls;

namespace HeyRead.Desktop.Views;

public sealed partial class MainWindow
{
    private const double CompactWindowWidth = 428;
    private const double MinimumCompactWidth = 360;
    private const double PreferredReaderWidth = 690;
    private const double MinimumReaderWidth = 360;
    private const double PaneInset = 4;
    private const double PaneGap = 4;

    private double _feedWidthWhileReading = CompactWindowWidth - 2 * PaneInset;
    private double _widthBeforeReader = CompactWindowWidth;
    private bool _readerLayoutOpen;
    private bool _expandedNormalWindow;
    private (int Before, int Expanded)? _readerPositionShift;

    private double FeedColumnWidth => _readerLayoutOpen
        ? _feedWidthWhileReading
        : Math.Max(0, (ClientSize.Width > 0 ? ClientSize.Width : Width) - 2 * PaneInset);

    private void PlaceCompactWindow()
    {
        if (Screens.Primary is not { } screen) return;
        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        var pairedWidth = Math.Min(area.Width, (CompactWindowWidth + PaneGap + PreferredReaderWidth) * scale);
        var visibleHeight = Math.Min(area.Height, Height * scale);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new(
            area.X + (int)Math.Round((area.Width - pairedWidth) / 2),
            area.Y + (int)Math.Round((area.Height - visibleHeight) / 2));
    }

    private void MatchWindowToReader(bool open)
    {
        if (_readerLayoutOpen == open) return;
        _readerLayoutOpen = open;

        if (!open)
        {
            if (!_expandedNormalWindow) return;
            _expandedNormalWindow = false;
            MinWidth = MinimumCompactWidth;
            Width = _widthBeforeReader;
            if (WindowState == WindowState.Normal && _readerPositionShift is { } shift && Position.X == shift.Expanded)
                Position = new PixelPoint(shift.Before, Position.Y);
            _readerPositionShift = null;
            return;
        }

        _widthBeforeReader = ClientSize.Width > 0 ? ClientSize.Width : Width;
        _feedWidthWhileReading = Math.Max(0, _widthBeforeReader - 2 * PaneInset);
        if (WindowState != WindowState.Normal)
        {
            _feedWidthWhileReading = Math.Min(_feedWidthWhileReading, CompactWindowWidth - 2 * PaneInset);
            return;
        }

        _expandedNormalWindow = true;
        var minimum = _widthBeforeReader + PaneGap + MinimumReaderWidth;
        var desired = _widthBeforeReader + PaneGap + PreferredReaderWidth;

        // Keep the feed's desktop coordinates unless the right edge leaves too little room to read.
        if ((Screens.ScreenFromPoint(Position) ?? Screens.ScreenFromWindow(this) ?? Screens.Primary) is { } screen)
        {
            var area = screen.WorkingArea;
            var scale = screen.Scaling;
            var rightRoom = (area.Right - Position.X) / scale;
            if (rightRoom < minimum && area.Width / scale >= minimum)
            {
                var x = area.Right - (int)Math.Ceiling(minimum * scale);
                _readerPositionShift = (Position.X, x);
                Position = new PixelPoint(x, Position.Y);
                rightRoom = (area.Right - x) / scale;
            }
            desired = Math.Min(desired, Math.Max(minimum, rightRoom));
        }

        MinWidth = minimum;
        Width = desired;
    }
}
