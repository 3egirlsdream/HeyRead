using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
namespace HeyRead.Desktop.Controls;

public static class Ui
{
    public static T Brush<T>(T control, AvaloniaProperty<IBrush?> property, string key) where T : Avalonia.StyledElement { control.Bind(property, control.GetResourceObservable(key)); return control; }
    public static TextBlock Text(string text, double size = 13, bool bold = false, bool muted = false) { var t = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, TextWrapping = TextWrapping.Wrap }; if (muted) t.Classes.Add("muted"); return t; }
    public static Button Button(string label, Action action, string? style = null) { var b = new Button { Content = label }; if (style != null) b.Classes.Add(style); b.Click += (_, _) => action(); return b; }
    public static Button AsyncButton(string label, Func<Task> action, string? style = null) { var b = new Button { Content = label }; if (style != null) b.Classes.Add(style); b.Click += async (_, _) => { b.IsEnabled = false; try { await action(); } finally { b.IsEnabled = true; } }; return b; }
    public static StackPanel Stack(double spacing = 12, params Control[] controls) { var s = new StackPanel { Spacing = spacing }; foreach (var c in controls) s.Children.Add(c); return s; }
    public static StackPanel Row(double spacing = 8, params Control[] controls) { var s = Stack(spacing, controls); s.Orientation = Orientation.Horizontal; s.VerticalAlignment = VerticalAlignment.Center; return s; }
    public static Border Line() => Brush(new Border { Height = 1 }, Border.BackgroundProperty, "Line");
    public static Border Group(params Control[] children) { var b = new Border { Child = Stack(12, children) }; b.Classes.Add("group"); return b; }
    public static Grid Columns(string columns, params Control[] children) { var g = new Grid { ColumnDefinitions = new(columns) }; for (var i = 0; i < children.Length; i++) { Grid.SetColumn(children[i], i); g.Children.Add(children[i]); } return g; }
}
