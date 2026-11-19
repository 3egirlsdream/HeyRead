using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using HeyRead.Desktop.Controls;
using LiquidGlassAvaloniaUI;
using static HeyRead.Desktop.Controls.Ui;

namespace HeyRead.Desktop.Views;

public sealed partial class MainWindow
{
    private readonly Grid _dockAnchor = new() { Margin = new(4, 0, 4, 4), VerticalAlignment = VerticalAlignment.Bottom };
    private readonly Dictionary<string, Button> _dockTabs = [];
    private Grid? _dockCapsule;

    private string DockPage => _vm.Page switch
    {
        "following" => "dynamic",
        "news" => "games",
        "discover" or "dynamic" or "games" or "search" or "account" => _vm.Page,
        _ => "more"
    };

    private void BuildDock()
    {
        const double height = 60;
        var tabs = new Grid { ColumnDefinitions = new("*,*,*,*,*,*"), Margin = new(4) };
        _dockTabs.Clear();
        foreach (var (page, label, icon, glyphSize) in new[]
        {
            ("discover", "发现", "dock_discover", 17), ("dynamic", "动态", "dock_activity", 19), ("games", "游戏", "dock_games", 20),
            ("search", "搜索", "dock_search", 18), ("account", "我的", "dock_account", 18), ("more", "更多", "dock_more", 17)
        })
        {
            var title = T(label);
            var button = page == "more"
                ? new Button { Classes = { "dockItem" } }
                : AsyncButton("", () => ActivateDockTabAsync(page), "dockItem");
            var caption = new Viewbox
            {
                Height = 18,
                StretchDirection = StretchDirection.DownOnly,
                Child = new TextBlock { Text = title, Classes = { "dockCaption" } }
            };
            var glyph = Icons.Get(icon, 20, "DockInk", glyphSize);
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            button.Content = new StackPanel
            {
                Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { glyph, caption }
            };
            Avalonia.Automation.AutomationProperties.SetName(button, title);
            Grid.SetColumn(button, tabs.Children.Count);
            tabs.Children.Add(button);
            _dockTabs.Add(page, button);
            if (page == "more") AttachDockMenu(button);
        }

        var glass = new LiquidGlassInteractiveSurface
        {
            Content = tabs,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height = height,
            CornerRadius = new(height / 2),
            BlurRadius = 7,
            Vibrancy = 1.28,
            RefractionAmount = 32,
            RefractionHeight = 19,
            DepthEffect = true,
            HighlightWidth = 1.4,
            HighlightAngle = 125,
            HighlightBlurRadius = .45,
            BackdropOpacity = 1,
            ShadowRadius = 12,
            ShadowOffset = new Vector(0, 4),
            ShadowColor = Color.FromArgb(47, 0, 0, 0),
            InnerShadowEnabled = false,
            IsInteractive = AllowMotion,
            InteractiveMaxScaleDip = 4
        };
        glass.Bind(LiquidGlassSurface.SurfaceColorProperty, glass.GetResourceObservable("DockGlassColor"));
        glass.Bind(LiquidGlassSurface.HighlightOpacityProperty, glass.GetResourceObservable("DockHighlightOpacity"));
        var edge = Brush(new Border
        {
            CornerRadius = new(height / 2), BorderThickness = new(1), IsHitTestVisible = false
        }, Border.BorderBrushProperty, "DockGlassEdge");
        _dockCapsule = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new(10, 0, 10, 10),
            Children = { glass, edge }
        };
        LiquidGlassBackdrop.SetIsExcludedFromCapture(_dockCapsule, true);
        _dockAnchor.Children.Clear();
        _dockAnchor.Children.Add(_dockCapsule);
        UpdateDock();
    }

    private void UpdateDock()
    {
        if (_dockCapsule == null) return;
        var placeOverFeed = HasReader;
        _dockAnchor.HorizontalAlignment = placeOverFeed ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        _dockAnchor.Width = placeOverFeed ? FeedColumnWidth : double.NaN;
        _dockCapsule.Width = Math.Clamp(FeedColumnWidth - 20, 0, 424);
        foreach (var (page, button) in _dockTabs) button.Classes.Set("selected", page == DockPage);
    }

    private async Task ActivateDockTabAsync(string page)
    {
        if (page == "search") { await OpenDockSearchAsync(); return; }
        if (page == DockPage && (page is "discover" or "dynamic" or "games"))
        {
            CloseReader();
            await RefreshDockPageAsync();
            return;
        }
        if (page == "games") _vm.Query = "";
        await Navigate(page, refresh: true);
    }

    private async Task OpenDockSearchAsync()
    {
        if (_vm.Page != "search" || !string.IsNullOrWhiteSpace(_vm.CurrentViewport.Query))
        {
            _vm.Query = "";
            await Navigate("search");
        }
        Dispatcher.UIThread.Post(() => _pageSearch?.Focus(), DispatcherPriority.Loaded);
    }

    private Task RefreshDockPageAsync()
    {
        SaveOffsets();
        _pendingListOffset = 0;
        _pagesWithoutVisibleRows = 0;
        ResetListPull();
        return _vm.LoadAsync();
    }

    private void AttachDockMenu(Button button)
    {
        var menu = new ContextMenu { Placement = PlacementMode.Top };
        MenuItem Action(string label, Func<Task> action)
        {
            var item = new MenuItem { Header = T(label) };
            item.Click += (_, _) => Run(action);
            return item;
        }
        menu.Items.Add(Action("返回", async () => { if (HasReader) CloseReader(); else { SaveOffsets(); await _vm.BackAsync(); } }));
        menu.Items.Add(Action("前进", async () => { SaveOffsets(); await _vm.BackAsync(true); }));
        menu.Items.Add(Action("刷新", RefreshDockPageAsync));
        menu.Items.Add(new Separator());
        menu.Items.Add(Action("导航收藏", () => Navigate("favorites", refresh: true)));
        menu.Items.Add(Action("历史", () => Navigate("history", refresh: true)));
        menu.Items.Add(Action("设置", () => Navigate("settings")));
        var channels = new MenuItem { Header = T("发现") };
        var channelsByKey = new Dictionary<string, MenuItem>();
        foreach (var (key, label) in new[] { ("all", "推荐"), ("news", "资讯"), ("community", "社区") })
        {
            var item = Action(label, async () =>
            {
                _vm.Channel = key;
                if (_vm.Page == "discover") { CloseReader(); await RefreshDockPageAsync(); }
                else await Navigate("discover", refresh: true);
            });
            item.ToggleType = MenuItemToggleType.Radio;
            item.GroupName = "DockChannels";
            channels.Items.Add(item);
            channelsByKey.Add(key, item);
        }
        menu.Items.Add(channels);

        var layouts = new MenuItem { Header = T("内容卡片布局") };
        var layoutsByKey = new Dictionary<string, MenuItem>();
        foreach (var key in new[] { "Compact", "Comfortable", "Gallery" })
        {
            var item = new MenuItem { Header = T(key), ToggleType = MenuItemToggleType.Radio, GroupName = "DockLayouts" };
            item.Click += (_, _) => { _vm.Prefs.CardLayout = key; _vm.SaveSettings(); };
            layouts.Items.Add(item);
            layoutsByKey.Add(key, item);
        }
        menu.Items.Add(layouts);
        menu.Items.Add(Action("阅读排版与字体", () => { _settingsTab = "reading"; return Navigate("settings"); }));
        menu.Opened += (_, _) =>
        {
            foreach (var (key, item) in channelsByKey) item.IsChecked = key == _vm.Channel;
            foreach (var (key, item) in layoutsByKey) item.IsChecked = key == _vm.Prefs.CardLayout;
        };
        button.ContextMenu = menu;
        button.Click += (_, _) => menu.Open(button);
    }
}
