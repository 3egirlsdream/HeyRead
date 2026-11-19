using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using HeyRead.Core;
using HeyRead.Desktop.Controls;
using static HeyRead.Desktop.Controls.Ui;

namespace HeyRead.Desktop.Views;

public sealed partial class MainWindow
{
    private static readonly Lazy<Bitmap> Landscape = new(() =>
    {
        using var stream = AssetLoader.Open(new Uri("avares://xiaoheihe-community-formac/Assets/landscape.jpg"));
        return new Bitmap(stream);
    });

    private Border Cover(double height) => new()
    {
        Height = height, CornerRadius = new(10), ClipToBounds = true,
        Child = new Image { Source = Landscape.Value, Stretch = Stretch.UniformToFill }
    };

    private Control BuildAccount()
    {
        var account = _vm.Api.Session;
        var panel = Stack(16);
        panel.Margin = new(18, 16, 18, 20);
        if (!_vm.Prefs.NoImages) panel.Children.Add(Cover(128));
        var identity = Stack(3, Text(account == null ? T("账号概览") : _vm.AccountName, 22, true));
        if (account != null)
        {
            var id = Button("ID · " + account.UserId, () => Run(async () =>
            {
                if (Clipboard != null) await Clipboard.SetTextAsync(account.UserId);
            }), "flat");
            ToolTip.SetTip(id, T("复制账号 ID"));
            identity.Children.Add(id);
            identity.Children.Add(Text(T("已登录小黑盒"), 14, muted: true));
        }
        else identity.Children.Add(AsyncButton(T("登录小黑盒"), LoginDialog, "primary"));
        var profile = Row(12, Avatar(_vm.Prefs.NoImages ? null : account?.Avatar, 62), identity);
        if (account == null) panel.Children.Add(profile);
        else
        {
            var following = AsyncButton(T("关注动态"), () => Navigate("following"), "flat");
            panel.Children.Add(Columns("*,Auto", profile, following));
        }

        var links = Stack(0);
        void Link(string icon, string label, Func<Task> open)
        {
            var b = AsyncButton("", open, "flat");
            b.Height = 40;
            b.Padding = new(12, 0);
            b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            var text = Text(T(label), 14, true);
            text.VerticalAlignment = VerticalAlignment.Center;
            b.Content = Columns("28,*,14", Icons.Get(icon, 17), text, Icons.Get("chevron_right", 13, "Muted"));
            links.Children.Add(b);
        }
        if (account != null)
        {
            Link("person", "我的主页", () => _vm.NavigateAsync("destination", new("author", account.UserId, account.Nickname)));
            links.Children.Add(Line());
            Link("bookmark", "云端收藏", () => Navigate("favorites"));
            links.Children.Add(Line());
            Link("history", "账号历史", () => Navigate("cloudhistory"));
            links.Children.Add(Line());
        }
        Link("book_open", "本机历史", () => Navigate("history"));
        var linkGroup = new Border { Child = links };
        linkGroup.Classes.Add("accountLinks");
        panel.Children.Add(linkGroup);
        if (account != null)
        {
            var logout = AsyncButton(T("退出登录"), async () =>
            {
                if (await Confirm(T("退出登录"), "退出当前账号？本机历史与阅读偏好会保留。")) _vm.Logout();
            });
            logout.MinWidth = 126;
            logout.Height = 32;
            logout.HorizontalAlignment = HorizontalAlignment.Center;
            var actions = Stack(12, Line(), logout);
            panel.Children.Add(actions);
        }
        return new ScrollViewer { Content = panel };
    }

    private Control SearchHistory()
    {
        var entries = _vm.Store.Searches.ToArray();
        var rows = Stack(0);
        var clear = Button("", () => { _vm.Store.ClearSearches(); Render(); }, "flat");
        clear.Content = Row(7, Icons.Get("delete", 18, "Muted"), Text(T("清除记录"), 15, muted: true));
        clear.Padding = new(6, 3);
        var heading = Text(T("搜索记录"), 21, true);
        heading.VerticalAlignment = VerticalAlignment.Center;
        var headingRow = Columns("*,Auto", heading, clear);
        headingRow.Margin = new(0, 0, 0, 10);
        rows.Children.Add(headingRow);
        if (entries.Length == 0) rows.Children.Add(Text(T("暂无搜索记录"), 14, muted: true));
        foreach (var entry in entries)
        {
            var replay = AsyncButton("", () => _vm.ReplaySearchAsync(entry), "flat");
            replay.HorizontalContentAlignment = HorizontalAlignment.Left;
            replay.Height = 38;
            replay.Padding = new(8, 0);
            var scope = entry.Steam ? "Steam" : entry.Scope?.Name ?? T("全站");
            var badge = Brush(new Border { Child = Text(scope, 14, muted: true), Padding = new(9, 2), CornerRadius = new(13) },
                Border.BackgroundProperty, entry.Scope == null && !entry.Steam ? "Selection" : "Subtle");
            replay.Content = Row(9, Icons.Get("search", 15, "Muted"), Text(entry.Query, 14, true), badge);
            var remove = Button("×", () => { _vm.Store.RemoveSearch(entry); Render(); }, "flat");
            Avalonia.Automation.AutomationProperties.SetName(remove, T("删除记录") + " " + entry.Query);
            rows.Children.Add(Columns("*,40", replay, remove));
            rows.Children.Add(Line());
        }
        return rows;
    }

    private Control BuildSearchLanding()
    {
        var field = new TextBox { PlaceholderText = T("搜索文章、作者或游戏"), MaxLength = 120, MaxWidth = 668, Height = 40, FontSize = 15, VerticalContentAlignment = VerticalAlignment.Center };
        field.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            _vm.Query = field.Text ?? "";
            SaveOffsets();
            await _vm.SearchAsync();
        };
        _pageSearch = field;
        var hero = new Grid();
        if (!_vm.Prefs.NoImages)
        {
            var backdrop = Cover(168);
            backdrop.CornerRadius = default;
            backdrop.OpacityMask = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = [new GradientStop(Colors.Black, 0.58), new GradientStop(Colors.Transparent, 1)]
            };
            hero.Children.Add(backdrop);
        }
        Brush(field, TextBox.BackgroundProperty, "Surface");
        field.InnerLeftContent = new Border { Child = Icons.Get("search", 17), Margin = new(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        field.VerticalAlignment = VerticalAlignment.Bottom;
        field.Margin = new(16, 14, 46, 12);
        hero.Children.Add(field);
        var scopes = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 6 };
        scopes.HorizontalAlignment = HorizontalAlignment.Center;
        scopes.Margin = new(14, 10, 14, 0);
        var candidates = _vm.Store.History.SelectMany(item =>
            (item.Story.Sections ?? []).Select(section => new Destination("section", section.Id, section.Name))
            .Concat((item.Story.Topics ?? []).Select(topic => new Destination("topic", topic.Id, topic.Name))))
            .Concat(_vm.Store.Searches.Select(entry => entry.Scope).OfType<Destination>()).ToArray();
        var scopeButtons = new Dictionary<string, Button>();
        var target = new ComboBox { MinWidth = 120, MaxWidth = 260 };
        var returnKey = Brush(new Border { Width = 36, Height = 28, CornerRadius = new(7), BorderThickness = new(1),
            Child = new TextBlock { Text = "↵", FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } },
            Border.BorderBrushProperty, "Line");
        var hint = Row(10, returnKey, Text(T("回车搜索"), 16, muted: true));
        hint.Margin = new(4, 0, 0, 0);
        void ShowTargets(string kind)
        {
            foreach (var (key, button) in scopeButtons)
                button.Classes.Set("primary", key == kind);
            target.IsVisible = kind.Length > 0;
            hint.IsVisible = !target.IsVisible;
            var items = candidates.Prepend(_vm.SearchScope).OfType<Destination>()
                .Where(item => item.Kind == kind && item.Id.Length > 0).DistinctBy(item => item.Id).ToArray();
            var preferred = _vm.SearchScope;
            target.ItemsSource = items;
            target.SelectedItem = items.FirstOrDefault(item => item.Id == preferred?.Id) ?? items.FirstOrDefault();
            _vm.SearchScope = target.SelectedItem as Destination;
            field.IsEnabled = kind.Length == 0 || target.SelectedItem != null;
            if (!field.IsEnabled) field.PlaceholderText = T("先浏览分区或话题，再在此范围搜索");
            else field.PlaceholderText = T("搜索文章、作者或游戏");
        }
        target.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Destination>((item, _) => Text(item?.Name ?? ""));
        target.SelectionChanged += (_, _) => _vm.SearchScope = target.SelectedItem as Destination;
        foreach (var (kind, label) in new[] { ("", "全站"), ("section", "分区"), ("topic", "话题") })
        {
            var button = Button(T(label), () => ShowTargets(kind));
            button.Classes.Add("sectionTab");
            button.MinWidth = 77;
            scopes.Children.Add(button);
            scopeButtons.Add(kind, button);
        }
        scopes.Children.Add(target);
        scopes.Children.Add(hint);
        ShowTargets(_vm.SearchScope?.Kind ?? "");
        var history = Stack(16, Line(), SearchHistory());
        history.Margin = new(18);
        return new ScrollViewer { Content = Stack(0, hero, scopes, history) };
    }

    private int? _previewGameId;
    private Control BuildGames()
    {
        var field = new TextBox
        {
            Text = _vm.Query, PlaceholderText = T("输入游戏名称或 AppID，浏览 Steam 社区公告。"),
            MaxLength = 120, VerticalContentAlignment = VerticalAlignment.Center
        };
        Avalonia.Automation.AutomationProperties.SetName(field, T("Steam 游戏"));
        field.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            SaveOffsets();
            _vm.Query = field.Text ?? "";
            await _vm.SearchAsync();
        };
        _pageSearch = field;
        var sidebar = Stack(12, Text(T("Steam 游戏"), 19, true), field);
        sidebar.Margin = new(16);
        if (_vm.Loading) sidebar.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 3 });
        if (_vm.Error.Length > 0) sidebar.Children.Add(Group(Text(_vm.Error), AsyncButton(T("重试"), () => _vm.LoadAsync())));
        if (_vm.Games.Count == 0)
        {
            if (!_vm.Loading) sidebar.Children.Add(SearchHistory());
            return new ScrollViewer { Content = sidebar };
        }
        var preview = new ContentControl { Margin = new(20) };
        var results = new ListBox { ItemsSource = _vm.Games };
        results.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Game>((game, _) =>
        {
            if (game == null) return new Border();
            var label = Stack(3, Text(game.Name, 14, true), Text($"AppID · {game.AppId}", 11, muted: true));
            label.VerticalAlignment = VerticalAlignment.Center;
            if (_vm.Prefs.NoImages) return label;
            if (_vm.Prefs.CardLayout == "Gallery") return Stack(8, Picture(game.Image, 132), label);
            if (_vm.Prefs.CardLayout == "Comfortable") return Columns("116,14,*", Picture(game.Image, 78, 116), new Border(), label);
            return Columns("68,9,*", Picture(game.Image, 46, 68), new Border(), label);
        });
        results.SelectionChanged += (_, _) =>
        {
            if (results.SelectedItem is not Game game) return;
            _previewGameId = game.AppId;
            var news = AsyncButton(T("查看公告"), () => _vm.NavigateAsync("news", game: game), "primary");
            var info = Stack(18, Columns("*,Auto", Stack(5, Text(game.Name, 22, true), Text($"Steam AppID · {game.AppId}", 14, muted: true)), news));
            if (!_vm.Prefs.NoImages && game.Image != null) info.Children.Add(Picture(game.Image, 260));
            preview.Content = info;
        };
        results.SelectedItem = _vm.Games.FirstOrDefault(x => x.AppId == _previewGameId) ?? _vm.Games[0];
        var master = new Grid { RowDefinitions = new("Auto,*") };
        master.Children.Add(sidebar);
        Grid.SetRow(results, 1);
        master.Children.Add(results);
        return Columns("2*,1,3*", master, Brush(new Border(), Border.BackgroundProperty, "Line"), new ScrollViewer { Content = preview });
    }
}
