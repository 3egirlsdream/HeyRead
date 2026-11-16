using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
namespace HeyRead.Desktop.Controls;

public static class Icons
{
    private static readonly Dictionary<string, string> Paths = JsonSerializer.Deserialize<Dictionary<string, string>>(Assembly.GetExecutingAssembly().GetManifestResourceStream("HeyRead.Desktop.Resources.icons.json")!) ?? [];
    public static Control Get(string name, double size = 20, string color = "Ink")
    {
        var data = Paths.GetValueOrDefault(name) ?? "M4 4h12v12H4z M6 6v8h8V6z";
        var p = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(data), Width = 20, Height = 20, Stretch = Stretch.Uniform };
        Ui.Brush(p, Avalonia.Controls.Shapes.Shape.FillProperty, color);
        return new Viewbox { Child = p, Width = size, Height = size };
    }
    public static Button Button(string icon, string tooltip, Func<Task> action)
    {
        var b = new Button { Content = Get(icon, 18), Width = 30, Height = 30, Padding = new Thickness(5) }; b.Classes.Add("flat"); ToolTip.SetTip(b, tooltip); Avalonia.Automation.AutomationProperties.SetName(b, tooltip);
        b.Click += async (_, _) => { try { await action(); } catch (Exception) { /* Action owner reports the error. */ } }; return b;
    }
}
