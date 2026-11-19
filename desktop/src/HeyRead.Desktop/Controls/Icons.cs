using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
namespace HeyRead.Desktop.Controls;

public static class Icons
{
    private static readonly Dictionary<string, string> Paths = JsonSerializer.Deserialize<Dictionary<string, string>>(Assembly.GetExecutingAssembly().GetManifestResourceStream("HeyRead.Desktop.Resources.icons.json")!) ?? [];
    public static Control Get(string name, double size = 20, string color = "Ink", double? glyphSize = null)
    {
        var data = Paths.GetValueOrDefault(name) ?? "M4 4h12v12H4z M6 6v8h8V6z";
        var geometry = Geometry.Parse(data);
        var bounds = geometry.Bounds;
        var scale = (glyphSize ?? size * .9) / Math.Max(bounds.Width, bounds.Height);
        var path = new Avalonia.Controls.Shapes.Path
        {
            Data = geometry, Width = bounds.Width * scale, Height = bounds.Height * scale,
            Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        Ui.Brush(path, Avalonia.Controls.Shapes.Shape.FillProperty, color);
        return new Grid { Width = size, Height = size, Children = { path } };
    }
    public static Button Button(string icon, string tooltip, Func<Task> action)
    {
        var b = new Button { Content = Get(icon, 16), Width = 30 }; b.Classes.Add("compactAction"); b.Classes.Add("iconOnly"); b.Classes.Add("flat"); ToolTip.SetTip(b, tooltip); Avalonia.Automation.AutomationProperties.SetName(b, tooltip);
        b.Click += async (_, _) => { try { await action(); } catch (Exception) { /* Action owner reports the error. */ } }; return b;
    }
}
