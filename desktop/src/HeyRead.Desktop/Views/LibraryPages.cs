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
        var panel = Stack(20);
        panel.Margin = new(24, 20, 24, 30);
        if (!_vm.Prefs.NoImages) panel.Children.Add(Cover(182));
        var identity = Stack(4, Text(account == null ? T("账号概览") : _vm.AccountName, 25, true));
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
        panel.Children.Add(Row(20, Avatar(_vm.Prefs.NoImages ? null : account?.Avatar, 88), identity));

        var links = Stack(0);
        void Link(string icon, string label, Func<Task> open)
        {
            var b = AsyncButton("", open, "flat");
            b.Height = 56;
            b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            b.Content = Columns("48,*,20", Icons.Get(icon, 22), Text(T(label), 17, true), Icons.Get("chevron_right", 16, "Muted"));
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
        if (account != null)
        {
            links.Children.Add(Line());
            Link("chat", "关注动态", () => Navigate("following"));
        }
        panel.Children.Add(Group(links));
        if (account != null)
        {
            var logout = AsyncButton(T("退出登录"), async () =>
            {
                if (await Confirm(T("退出登录"), "退出当前账号？本机历史与阅读偏好会保留。")) _vm.Logout();
            });
            logout.Width = 210;
            logout.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(logout);
        }
        return new ScrollViewer { Content = panel };
    }

    private Control SearchHistory()
    {
        var entries = _vm.Store.Searches.ToArray();
        var rows = Stack(2);
        rows.Children.Add(Columns("*,Auto", Text(T("搜索记录"), 18, true),
            Button(T("清除记录"), () => { _vm.Store.ClearSearches(); Render(); }, "flat")));
        if (entries.Length == 0) rows.Children.Add(Text(T("暂无搜索记录"), 14, muted: true));
        foreach (var entry in entries)
        {
            var replay = AsyncButton("", () => _vm.ReplaySearchAsync(entry), "flat");
            replay.HorizontalContentAlignment = HorizontalAlignment.Left;
            replay.Content = Row(12, Icons.Get("search", 18), Text(entry.Query, 16), Text(entry.Steam ? "Steam" : entry.Scope?.Name ?? T("全站"), 13, muted: true));
            var remove = Button("×", () => { _vm.Store.RemoveSearch(entry); Render(); }, "flat");
            Avalonia.Automation.AutomationProperties.SetName(remove, T("删除记录") + " " + entry.Query);
            rows.Children.Add(Columns("*,40", replay, remove));
            rows.Children.Add(Line());
        }
        return rows;
    }

    private Control BuildSearchLanding()
    {
        var field = new TextBox { PlaceholderText = T("搜索文章、作者或游戏"), MaxLength = 120, MaxWidth = 670, Height = 45 };
        field.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            _vm.Query = field.Text ?? "";
            _search.Text = field.Text;
            await _vm.SearchAsync();
        };
        var hero = new Grid();
        if (!_vm.Prefs.NoImages) hero.Children.Add(Cover(234));
        Brush(field, TextBox.BackgroundProperty, "Surface");
        field.InnerLeftContent = new Border { Child = Icons.Get("search", 20, "Muted"), Margin = new(10, 0, 8, 0) };
        field.VerticalAlignment = VerticalAlignment.Bottom;
        field.Margin = new(24, 16);
        hero.Children.Add(field);
        var scopes = Row(10);
        scopes.HorizontalAlignment = HorizontalAlignment.Center;
        scopes.Margin = new(8, 12);
        var candidates = _vm.Store.History.SelectMany(item =>
            (item.Story.Sections ?? []).Select(section => new Destination("section", section.Id, section.Name))
            .Concat((item.Story.Topics ?? []).Select(topic => new Destination("topic", topic.Id, topic.Name))))
            .Concat(_vm.Store.Searches.Select(entry => entry.Scope).OfType<Destination>()).ToArray();
        var scopeButtons = new Dictionary<string, Button>();
        var target = new ComboBox { MinWidth = 120, MaxWidth = 260 };
        void ShowTargets(string kind)
        {
            foreach (var (key, button) in scopeButtons)
                button.Classes.Set("primary", key == kind);
            target.IsVisible = kind.Length > 0;
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
            button.MinWidth = 70;
            scopes.Children.Add(button);
            scopeButtons.Add(kind, button);
        }
        scopes.Children.Add(target);
        ShowTargets(_vm.SearchScope?.Kind ?? "");
        var history = SearchHistory();
        history.Margin = new(64, 26);
        return new ScrollViewer { Content = Stack(0, hero, scopes, history) };
    }

    private int? _previewGameId;
    private Control BuildGames()
    {
        var sidebar = Stack(12, Text(T("Steam 游戏"), 19, true));
        sidebar.Margin = new(16);
        if (_vm.Loading) sidebar.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 3 });
        if (_vm.Error.Length > 0) sidebar.Children.Add(Group(Text(_vm.Error), AsyncButton(T("重试"), () => _vm.LoadAsync())));
        if (_vm.Games.Count == 0)
        {
            sidebar.Children.Add(Text(T("输入游戏名称或 AppID，浏览 Steam 社区公告。"), 14, muted: true));
            if (!_vm.Loading) sidebar.Children.Add(SearchHistory());
            return new ScrollViewer { Content = sidebar };
        }
        var preview = new ContentControl { Margin = new(20) };
        var results = new ListBox { ItemsSource = _vm.Games };
        results.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Game>((game, _) =>
        {
            if (game == null) return new Border();
            var label = Stack(5, Text(game.Name, 15, true), Text($"AppID · {game.AppId}", 12, muted: true));
            if (_vm.Prefs.NoImages) return label;
            if (_vm.Prefs.CardLayout == "Gallery") return Stack(8, Picture(game.Image, 132), label);
            if (_vm.Prefs.CardLayout == "Comfortable") return Columns("116,14,*", Picture(game.Image, 78, 116), new Border(), label);
            return Columns("94,12,*", Picture(game.Image, 60, 94), new Border(), label);
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
