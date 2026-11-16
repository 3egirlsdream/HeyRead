using Avalonia.Input.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using HeyRead.Core;
using HeyRead.Desktop.Controls;
using static HeyRead.Desktop.Controls.Ui;

namespace HeyRead.Desktop.Views;
public sealed partial class MainWindow
{
    private Control BuildHeader()
    {
        var back = ToolbarButton("chevron_left", "返回", async () => { SaveOffsets(); if (_narrow && _showDetail) { SaveOffsets(); _showDetail = false; UpdateResponsive(); } else await _vm.BackAsync(); });
        var forward = ToolbarButton("chevron_right", "前进", async () => { SaveOffsets(); await _vm.BackAsync(true); });
        var refresh = ToolbarButton("arrow_clockwise", "刷新", () => _vm.LoadAsync());
        _search.PlaceholderText = T("搜索文章、作者或游戏"); _search.Text = _vm.Query; _search.Height = 36; _search.FontSize = 16;
        _search.InnerLeftContent = new Border { Child = Icons.Get("search", 20, "Muted"), Margin = new(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        _search.Classes.Add("search");
        var layout = new Button { Content = Row(14, Icons.Get("list", 20), Icons.Get("chevron_down", 16)), Height = 36, Width = 76, Padding = new(12, 4) };
        ToolTip.SetTip(layout, T("内容卡片布局"));
        var menu = new ContextMenu();
        foreach (var value in new[] { "Compact", "Comfortable", "Gallery" }) { var item = new MenuItem { Header = T(value), ToggleType = MenuItemToggleType.Radio, IsChecked = _vm.Prefs.CardLayout == value }; item.Click += (_, _) => { _vm.Prefs.CardLayout = value; _vm.SaveSettings(); }; menu.Items.Add(item); }
        menu.Items.Add(new Separator());
        var community = new MenuItem { Header = T("社区") }; community.Click += (_, _) => Run(async () => { await Navigate("discover"); _vm.Channel = "community"; await _vm.LoadAsync(); }); menu.Items.Add(community);
        var reading = new MenuItem { Header = T("阅读排版与字体") }; reading.Click += (_, _) => Run(async () => { _settingsTab = "reading"; await Navigate("settings"); }); menu.Items.Add(reading);
        layout.ContextMenu = menu; layout.Click += (_, _) => menu.Open(layout);
        var grid = new Grid { ColumnDefinitions = new("42,6,42,24,*,24,42,24,76"), Margin = new(20, 14, 16, 18) };
        foreach (var (control, col) in new[] { ((Control)back, 0), (forward, 2), (_search, 4), (refresh, 6), (layout, 8) }) { Grid.SetColumn(control, col); grid.Children.Add(control); }
        var border = Brush(new Border { Child = grid, BorderThickness = new(0, 0, 0, 1) }, Border.BorderBrushProperty, "Line");
        border.PointerPressed += (_, e) => { if (e.Source == border && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        return border;
    }
    private Button ToolbarButton(string icon, string label, Func<Task> action)
    {
        var button = AsyncButton("", action); button.Content = Icons.Get(icon, 20); button.Width = 42; button.Height = 36; button.Padding = new(8, 5);
        ToolTip.SetTip(button, T(label)); Avalonia.Automation.AutomationProperties.SetName(button, T(label)); return button;
    }
    private Control Picture(string? url, double height, double? width = null)
    {
        if (_vm.VisualFixture && url?.StartsWith("design:") == true) return _referenceAssets.Artwork(url[7..], height, width);
        var image = new RemoteImage(_images, url, height); if (width != null) image.Width = width.Value; return image;
    }
    private Control Avatar(string? url, double size)
    {
        var content = url == null ? (Control)Icons.Get("person", size * .6, "Muted") : Picture(url, size, size);
        return Brush(new Border { Child = content, Width = size, Height = size, CornerRadius = new(size / 2), ClipToBounds = true }, Border.BackgroundProperty, "Subtle");
    }
    private void RenderSidebar()
    {
        var logo = Brush(new Border { Child = Icons.Get("book_open_filled", 25, "OnAccent"), Width = 39, Height = 39, CornerRadius = new(12), Padding = new(7) }, Border.BackgroundProperty, "AccentFill");
        var brandName = Text("小黑盒", 20, true); brandName.LineHeight = 22; var brandSubtitle = Text("Community For Mac", 11.5, muted: true); brandSubtitle.LineHeight = 20;
        var brand = Row(14, logo, Stack(0, brandName, brandSubtitle)); brand.Margin = new(26, 64, 18, 30);
        var nav = Stack(8); nav.Margin = new(13, 0);
        foreach (var (page, label, icon) in new[] { ("discover", "发现", "compass_northwest"), ("dynamic", "动态", "chat"), ("games", "游戏", "games"), ("favorites", "导航收藏", "bookmark"), ("history", "历史", "history") })
        {
            var selected = _vm.Page == page || page == "dynamic" && _vm.Page == "following" || page == "games" && _vm.Page == "news" || page == "history" && _vm.Page == "cloudhistory";
            var b = Button("", () => Run(() => Navigate(page)), "nav"); b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.Content = Row(18, Icons.Get(icon, 25, selected ? "Accent" : "Ink"), Ui.Brush(Text(T(label), 18, selected), TextBlock.ForegroundProperty, selected ? "Accent" : "Ink"));
            if (selected) b.Classes.Add("selected");
            nav.Children.Add(b);
        }
        var accountName = Text(_vm.VisualFixture ? "林间" : T(_vm.AccountName), 18); accountName.MaxLines = 1; accountName.TextTrimming = TextTrimming.CharacterEllipsis; ToolTip.SetTip(accountName, _vm.VisualFixture ? "林间" : T(_vm.AccountName));
        var account = Button("", () => Run(() => Navigate("account")), "nav"); account.Content = Columns("32,14,*,20", Avatar(_vm.VisualFixture ? "design:account" : _vm.Api.Session?.Avatar, 32), new Border(), accountName, Icons.Get("chevron_right", 18, "Muted")); account.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var settings = Button("", () => Run(() => Navigate("settings")), "nav"); settings.Content = Row(18, Icons.Get("settings", 25), Text(T("设置"), 18));
        var line = Line(); line.Margin = new(0, 0, 0, 12);
        var bottom = Stack(8, line, account, settings); bottom.Margin = new(13, 0, 13, 24);
        var content = new Grid { RowDefinitions = new("Auto,Auto,*,Auto") }; content.Children.Add(brand); Grid.SetRow(nav, 1); content.Children.Add(nav); Grid.SetRow(bottom, 3); content.Children.Add(bottom);
        _sidebar.Content = Brush(Brush(new Border { Child = content, BorderThickness = new(0, 0, 1, 0) }, Border.BackgroundProperty, "Sidebar"), Border.BorderBrushProperty, "Line");
    }
    private Control StoryRow(Story? story)
    {
        if (story == null) return new Border();
        var compactList = _vm.Prefs.ListWidth < 380;
        var title = Text(story.Title, 16, true); title.MaxLines = ReaderWidth < 700 && _vm.Prefs.ListWidth >= 400 ? 1 : 2; title.LineHeight = 21; title.TextTrimming = TextTrimming.CharacterEllipsis; ToolTip.SetTip(title, story.Title);
        var author = Text(story.Author + " · " + story.Date, compactList ? 12.5 : 13.5, muted: true); author.MaxLines = 1; author.LineHeight = 18; author.MaxWidth = Math.Max(70, _vm.Prefs.ListWidth - (compactList ? 169 : 198)); author.TextTrimming = TextTrimming.CharacterEllipsis;
        var metadata = new Grid { ColumnDefinitions = new("Auto,Auto"), ColumnSpacing = 8 }; metadata.Children.Add(author);
        if (story.CommentCount != null) { var comments = Row(5, Icons.Get("comment", 14, "Muted"), Text(story.CommentCount.Value.ToString(), 13.5, muted: true)); Grid.SetColumn(comments, 1); metadata.Children.Add(comments); }
        var detail = Stack(8, title, metadata); detail.VerticalAlignment = VerticalAlignment.Center;
        Control content = detail;
        if (!_vm.Prefs.NoImages && story.Image != null)
        {
            content = _vm.Prefs.CardLayout == "Gallery" ? Stack(12, Picture(story.Image, 158), detail) : Columns(compactList ? "94,12,*" : "100,16,*", Picture(story.Image, 68, compactList ? 94 : 100), new Border(), detail);
        }
        if (_vm.Prefs.CardLayout == "Comfortable") content = Stack(10, content, Text(story.Excerpt, 14, muted: true));
        var card = new Border { Child = content, Padding = new(compactList ? 8 : 11, 12), MinHeight = _vm.Prefs.CardLayout == "Compact" ? 92 : 132, CornerRadius = new(10) };
        card.Classes.Add("storyCard");
        var divider = Line(); divider.Margin = new(8, 8, 8, 0);
        var outer = Stack(0, card, divider); outer.Margin = new(6, 0, 6, 0);
        var menu = new ContextMenu();
        foreach (var (label, action) in new (string, Action)[] { ("打开原文", () => OpenUrl(story.Url)), ("复制链接", () => Run(() => Clipboard?.SetTextAsync(story.Url ?? "") ?? Task.CompletedTask)), ("屏蔽关键词", () => Run(() => RuleDialog(new(RuleKind.KEYWORD, "")))), ("屏蔽作者", () => { if (story.Profile?.Id is { Length: > 0 } id) AddRule(new(RuleKind.AUTHOR, id, story.Author, Source: Source.HEYBOX)); }) }) { var item = new MenuItem { Header = T(label) }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        outer.ContextMenu = menu; return outer;
    }
}
