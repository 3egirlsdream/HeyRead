using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using HeyRead.Core;
using HeyRead.Desktop.Controls;
using HeyRead.Desktop.Platform;
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
                var sample = ReadingText(T("当我们把阅读的节奏放慢，文字之间的留白，也成为故事的一部分。"));
                var second = ReadingText("A quiet space for your next story.");
                void Preview()
                {
                    sample.FontSize = second.FontSize = p.BodySize;
                    sample.LineHeight = second.LineHeight = p.LineHeight;
                    second.Margin = new(0, p.ParagraphSpacing, 0, 0);
                }
                var lineHeight = NumberInput("行高", p.BodySize * 1.2, p.BodySize * 2.4, p.LineHeight, value => { p.LineHeight = value; Preview(); });
                var bodySize = NumberInput("正文字号", 14, 26, p.BodySize, value =>
                {
                    p.BodySize = value;
                    lineHeight.Maximum = (decimal)(value * 2.4);
                    lineHeight.Minimum = (decimal)(value * 1.2);
                    Preview();
                });
                var paragraph = NumberInput("段落间距", 8, 40, p.ParagraphSpacing, value => { p.ParagraphSpacing = value; Preview(); });
                content.Children.Add(Group(Columns("*,Auto", Text(T("正文字号")), bodySize),
                    Columns("*,Auto", Text(T("行高")), lineHeight), Columns("*,Auto", Text(T("段落间距")), paragraph)));
                Preview();
                var fonts = new[] { "System", "Sans", "Serif", "Mono" }.Concat(Directory.GetFiles(Path.Combine(_vm.Store.Root, "fonts")).Select(x => Path.GetFileName(x))).ToArray();
                content.Children.Add(Group(Choice("正文字体", fonts, p.BodyFont, x => p.BodyFont = x), Choice("界面字体", fonts, p.UiFont, x => p.UiFont = x), AsyncButton(T("导入字体"), async () => { var files = await StorageProvider.OpenFilePickerAsync(new() { Title = T("导入字体"), FileTypeFilter = [new(T("字体")) { Patterns = ["*.ttf", "*.otf", "*.ttc"] }] }); if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) { if (OperatingSystem.IsMacOS() && Native.InspectFont(path) == null) { await Notice(T("字体文件无效，请选择其他字体")); return; } p.BodyFont = _vm.Store.ImportFont(path); _vm.SaveSettings(); Render(); } })));
                foreach (var font in fonts.Skip(4)) content.Children.Add(Row(12, Text(font), AsyncButton(T("移除字体"), async () => { if (await Confirm(T("移除字体"), F("移除 {0}？使用该字体的选项将恢复系统字体。", font))) { if (p.BodyFont == font) p.BodyFont = "System"; if (p.UiFont == font) p.UiFont = "System"; _vm.SaveSettings(); Render(); var path = Path.Combine(_vm.Store.Root, "fonts", font); _fontFamilies.Remove(path); Native.UnregisterFont(path); File.Delete(path); Render(); } })));
                content.Children.Add(Group(Text(T("阅读预览"), 16, true), Stack(0, sample, second))); break;
            case "data":
                content.Children.Add(Text(T("数据来源与缓存"), 24, true));
                var gateway = new TextBox { Text = p.Gateway };
                content.Children.Add(Group(Text(T("兼容网关"), 14, true), gateway, Text(T("仅公开内容使用此来源；账号请求固定使用小黑盒官方 HTTPS 接口。"), 12, muted: true), AsyncButton(T("保存"), async () => { _ = HeyReadApi.SignedUri(gateway.Text ?? "", "/bbs/app/feeds"); p.Gateway = gateway.Text!.TrimEnd('/'); _vm.SaveSettings(); await Notice("已保存数据来源"); })));
                content.Children.Add(Group(Text(F("图片缓存 · {0:F1} MiB", _vm.Store.CacheBytes() / 1048576.0), 14, true), AsyncButton(T("清理缓存"), async () => { if (await Confirm(T("清理缓存"), "清理下载的图片缓存？阅读偏好、历史和登录状态会保留。")) { _vm.Store.ClearCache(); Render(); } }), Toggle("记录本机历史", p.RecordHistory, x => p.RecordHistory = x), AsyncButton(T("清空本机历史"), async () => { if (await Confirm(T("清空本机历史"), "删除所有本机阅读记录？此操作无法撤销。")) { _vm.Store.ClearHistory(); Render(); } }))); break;
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
                content.Children.Add(Text(T(ProductInfo.DisplayName), 28, true)); content.Children.Add(Text("macOS · " + ProductInfo.Version, 14, muted: true)); content.Children.Add(BuildUpdateSettings());
                content.Children.Add(Group(Text("Avalonia 12.1.6 · .NET 10.0.201"), Text(T("基于 23ye/HeyRead 独立开发的 macOS 客户端。上游代码采用 MIT 许可证。"), 13), Button("GitHub", () => OpenUrl("https://github.com/3egirlsdream/HeyRead")), Button(T("上游项目"), () => OpenUrl("https://github.com/23ye/HeyRead"))));
                content.Children.Add(Group(Text(T("第三方说明"), 17, true), Text(T("HeyRead · MIT\nAvalonia · MIT\n.NET · MIT\nCommunityToolkit.Mvvm · MIT\nAngleSharp · MIT\nQRCoder · MIT\nMicrosoft Fluent System Icons · MIT\nApple Translation、AVKit、AppKit 为系统框架。")), Text(T("FKFinder 用作界面与工程参考；新增组件独立实现。"), 12, muted: true))); break;
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
    private NumericUpDown NumberInput(string label, double min, double max, double value, Action<double> set)
    {
        var number = new NumericUpDown { Minimum = (decimal)min, Maximum = (decimal)max, Value = (decimal)value, Increment = 1, FormatString = "0.#", Width = 110 };
        Avalonia.Automation.AutomationProperties.SetName(number, T(label));
        number.ValueChanged += (_, _) => { if (number.Value is { } v) { set((double)v); _vm.Store.SavePreferences(); } }; return number;
    }
}
