using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using HeyRead.Core;
using HeyRead.Desktop.Controls;
using static HeyRead.Desktop.Controls.Ui;
namespace HeyRead.Desktop.Views;

public sealed partial class MainWindow
{
    private string _settingsTab = "appearance";
    private Control BuildSettings()
    {
        var nav = Stack(6); nav.Margin = new(20, 24);
        foreach (var (id, text) in new[] { ("appearance", "外观"), ("reading", "阅读排版与字体"), ("data", "数据来源与缓存"), ("blocking", "内容屏蔽"), ("about", "关于及第三方说明") }) nav.Children.Add(Button(T(text), () => { _settingsTab = id; Render(); }, id == _settingsTab ? "primary" : "nav"));
        var content = Stack(24); content.Margin = new(28); content.MaxWidth = 700; content.HorizontalAlignment = HorizontalAlignment.Stretch;
        var p = _vm.Prefs;
        switch (_settingsTab)
        {
            case "appearance":
                content.Children.Add(Text(T("外观"), 24, true));
                content.Children.Add(Group(Choice("主题", ["System", "Light", "Dark"], p.Theme, x => p.Theme = x), Choice("强调色", ["System", "Blue", "Yellow", "Green", "Purple"], p.Accent, x => p.Accent = x), Choice("语言", ["zh-CN", "zh-TW", "en", "fr"], p.Language, x => p.Language = x)));
                content.Children.Add(Group(Choice("内容卡片布局", ["Compact", "Comfortable", "Gallery"], p.CardLayout, x => p.CardLayout = x), Toggle("无图模式", p.NoImages, x => p.NoImages = x), Toggle("界面动画", p.Animations, x => p.Animations = x), Toggle("恢复导航", p.RestoreNavigation, x => p.RestoreNavigation = x))); break;
            case "reading":
                content.Children.Add(Text(T("阅读排版与字体"), 24, true));
                content.Children.Add(Group(Number("正文字号", 14, 26, p.BodySize, x => { p.BodySize = x; p.LineHeight = Math.Clamp(p.LineHeight, x * 1.2, x * 2.4); }), Number("行高", p.BodySize * 1.2, p.BodySize * 2.4, p.LineHeight, x => p.LineHeight = x), Number("段落间距", 8, 40, p.ParagraphSpacing, x => p.ParagraphSpacing = x)));
                var fonts = new[] { "System", "Sans", "Serif", "Mono" }.Concat(Directory.GetFiles(Path.Combine(_vm.Store.Root, "fonts")).Select(x => Path.GetFileName(x))).ToArray();
                content.Children.Add(Group(Choice("正文字体", fonts, p.BodyFont, x => p.BodyFont = x), Choice("界面字体", fonts, p.UiFont, x => p.UiFont = x), AsyncButton(T("导入字体"), async () => { var files = await StorageProvider.OpenFilePickerAsync(new() { Title = T("导入字体"), FileTypeFilter = [new("字体") { Patterns = ["*.ttf", "*.otf", "*.ttc"] }] }); if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) { p.BodyFont = _vm.Store.ImportFont(path); _vm.SaveSettings(); Render(); } })));
                foreach (var font in fonts.Skip(4)) content.Children.Add(Row(12, Text(font), AsyncButton(T("移除字体"), async () => { if (await Confirm(T("移除字体"), $"移除 {font}？使用该字体的选项将恢复系统字体。")) { if (p.BodyFont == font) p.BodyFont = "System"; if (p.UiFont == font) p.UiFont = "System"; File.Delete(Path.Combine(_vm.Store.Root, "fonts", font)); _vm.SaveSettings(); Render(); } })));
                content.Children.Add(Group(Text(T("阅读预览"), 16, true), ReadingText("当我们把阅读的节奏放慢，文字之间的留白，也成为故事的一部分。\n\nA quiet space for your next story."))); break;
            case "data":
                content.Children.Add(Text(T("数据来源与缓存"), 24, true));
                var gateway = new TextBox { Text = p.Gateway };
                content.Children.Add(Group(Text(T("兼容网关"), 14, true), gateway, Text("仅公开内容使用此来源；账号请求固定使用小黑盒官方 HTTPS 接口。", 12, muted: true), AsyncButton(T("保存"), async () => { _ = HeyReadApi.SignedUri(gateway.Text ?? "", "/bbs/app/feeds"); p.Gateway = gateway.Text!.TrimEnd('/'); _vm.SaveSettings(); await Notice("已保存数据来源"); })));
                content.Children.Add(Group(Text($"图片缓存 · {_vm.Store.CacheBytes() / 1048576.0:F1} MiB", 14, true), AsyncButton(T("清理缓存"), async () => { if (await Confirm(T("清理缓存"), "清理下载的图片缓存？阅读偏好、历史和登录状态会保留。")) { _vm.Store.ClearCache(); Render(); } }), Toggle("记录本机历史", p.RecordHistory, x => p.RecordHistory = x), AsyncButton(T("清空本机历史"), async () => { if (await Confirm(T("清空本机历史"), "删除所有本机阅读记录？此操作无法撤销。")) { _vm.Store.ClearHistory(); Render(); } }))); break;
            case "blocking":
                content.Children.Add(Text(T("内容屏蔽"), 24, true));
                content.Children.Add(Group(Toggle("启用屏蔽", p.BlockingEnabled, x => p.BlockingEnabled = x), Toggle("应用到收藏", p.BlockFavorites, x => p.BlockFavorites = x), Toggle("应用到历史", p.BlockHistory, x => p.BlockHistory = x)));
                content.Children.Add(Row(8, AsyncButton(T("添加规则"), () => RuleDialog(), "primary"), AsyncButton(T("导入规则"), ImportRules), AsyncButton(T("导出规则"), ExportRules)));
                content.Children.Add(Text($"{p.Rules.Count} / 500", 12, muted: true));
                foreach (var rule in p.Rules.ToArray())
                {
                    var enabled = new CheckBox { Content = $"{rule.Kind} · {(rule.Label.Length > 0 ? rule.Label + " · " : "")}{rule.Value}", IsChecked = rule.Enabled, MaxWidth = 380 };
                    enabled.IsCheckedChanged += (_, _) => { var i = p.Rules.IndexOf(rule); if (i >= 0) p.Rules[i] = rule with { Enabled = enabled.IsChecked == true }; _vm.Store.SavePreferences(); };
                    content.Children.Add(Group(Row(8, enabled, AsyncButton(T("编辑"), () => RuleDialog(rule), "flat"), Button(T("删除"), () => { p.Rules.Remove(rule); _vm.SaveSettings(); Render(); }, "flat"))));
                }
                break;
            default:
                content.Children.Add(Text("盒阅 · HeyRead", 28, true)); content.Children.Add(Text("macOS · 0.1.0", 14, muted: true));
                content.Children.Add(Group(Text("Avalonia 12.1.6 · .NET 10.0.201"), Text("基于 23ye/HeyRead 独立开发的 macOS 客户端。上游代码采用 MIT 许可证。", 13), Button("GitHub", () => OpenUrl("https://github.com/3egirlsdream/HeyRead")), Button("上游项目", () => OpenUrl("https://github.com/23ye/HeyRead"))));
                content.Children.Add(Group(Text(T("第三方说明"), 17, true), Text("HeyRead · MIT\nAvalonia · MIT\n.NET · MIT\nCommunityToolkit.Mvvm · MIT\nAngleSharp · MIT\nQRCoder · MIT\nMicrosoft Fluent System Icons · MIT\nApple Translation、AVKit、AppKit 为系统框架。"), Text("FKFinder 用作界面与工程参考；新增组件独立实现。", 12, muted: true))); break;
        }
        var grid = Columns("200,*", nav, new ScrollViewer { Content = content }); return grid;
    }
    private Control Toggle(string label, bool value, Action<bool> set)
    {
        var check = new ToggleSwitch { IsChecked = value, OnContent = "", OffContent = "", HorizontalAlignment = HorizontalAlignment.Right };
        Avalonia.Automation.AutomationProperties.SetName(check, T(label));
        check.IsCheckedChanged += (_, _) => { set(check.IsChecked == true); _vm.SaveSettings(); };
        var text = Text(T(label)); text.VerticalAlignment = VerticalAlignment.Center; return Columns("*,Auto", text, check);
    }
    private sealed record ChoiceOption(string Value, string Label) { public override string ToString() => Label; }
    private Control Choice(string label, string[] values, string selected, Action<string> set)
    {
        var combo = new ComboBox { ItemsSource = values.Select(x => new ChoiceOption(x, T(x))).ToArray(), SelectedIndex = Math.Max(0, Array.IndexOf(values, selected)), Width = 170 }; Avalonia.Automation.AutomationProperties.SetName(combo, T(label));
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is ChoiceOption option && option.Value != selected) { set(option.Value); _vm.SaveSettings(); } }; var text = Text(T(label)); text.VerticalAlignment = VerticalAlignment.Center; return Columns("*,Auto", text, combo);
    }
    private Control Number(string label, double min, double max, double value, Action<double> set)
    {
        var number = new NumericUpDown { Minimum = (decimal)min, Maximum = (decimal)max, Value = (decimal)value, Increment = 1, FormatString = "0.#", Width = 110 };
        Avalonia.Automation.AutomationProperties.SetName(number, T(label));
        number.ValueChanged += (_, _) => { if (number.Value is { } v) { set((double)v); _vm.Store.SavePreferences(); } }; var text = Text(T(label)); text.VerticalAlignment = VerticalAlignment.Center; return Columns("*,Auto", text, number);
    }
}
