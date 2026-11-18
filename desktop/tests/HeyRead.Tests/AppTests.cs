using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using HeyRead.Desktop.Controls;
using System.Net;
using System.Text;
using Avalonia.VisualTree;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Headless.XUnit;
using HeyRead.Core;
using HeyRead.Desktop;
using HeyRead.Desktop.Platform;
using HeyRead.Desktop.ViewModels;
using HeyRead.Desktop.Views;
using Xunit;
[assembly: AvaloniaTestApplication(typeof(HeyRead.Tests.TestAppBuilder))]
namespace HeyRead.Tests;

public static class TestAppBuilder { public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()); }
public sealed class AppTests
{
    [AvaloniaFact]
    public async Task CommentComposerRestoresDraftAfterSwitchingToArticleAndPostsToTheSelectedStory()
    {
        var root = Directory.CreateTempSubdirectory("heyread-composer-");
        var store = new LibraryStore(root.FullName);
        store.Preferences.NoImages = true;
        var publicFeed = new FakeHandler("""{"status":"ok","result":{"links":[]}}""");
        var account = new FakeHandler("""{"status":"ok","result":{"link":{"content":"<p>Article</p>"}}}""");
        using var api = new HeyReadApi(publicFeed, account);
        api.SetSession(new("pkey=test", "42", "Fixture", ""));
        using var vm = new MainViewModel(api, new(api), store, new MemorySessionStore(), new NoTranslation());
        var window = new MainWindow(vm);
        try
        {
            window.Show();
            await vm.SelectAsync(new Story("55", Source.HEYBOX, "Draft article", "Summary"));
            vm.CommentsMode = true;
            TextBox Editor()
            {
                window.UpdateLayout();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                return window.GetVisualDescendants().OfType<TextBox>().Single(box => box.MaxLength == 10000);
            }
            Editor().Text = "saved thought";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            vm.CommentsMode = false;
            vm.CommentsMode = true;
            Assert.Equal("saved thought", Editor().Text);
            var send = window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "发送"));
            send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; i < 100 && !send.IsEnabled; ++i) await Task.Delay(10, TestContext.Current.CancellationToken);
            var request = Assert.Single(account.Requests, r => r.Uri.AbsolutePath.EndsWith("/comment/create"));
            Assert.Contains("link_id=55", request.Form);
            Assert.Contains("text=saved", request.Form);
            Assert.Empty(new LibraryStore(root.FullName).Drafts);
        }
        finally { window.Close(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); root.Delete(true); }
    }

    [AvaloniaFact]
    public async Task WindowCanNavigateRepeatedlyAndChangeThemesWithoutReparenting()
    {
        var root = Path.Combine(Path.GetTempPath(), "heyread-test-" + Guid.NewGuid()); var store = new LibraryStore(root); store.Preferences.NoImages = true;
        using var api = new HeyReadApi(new FakeHandler("{\"status\":\"ok\",\"result\":{\"links\":[]}}"));
        using var vm = new MainViewModel(api, new(api), store, new MemorySessionStore(), new NoTranslation());
        var window = new MainWindow(vm); window.Show();
        await vm.NavigateAsync("discover");
        vm.Stories.Add(DemoStories.Items[0]); window.Measure(new(1280, 800)); window.Arrange(new(0, 0, 1280, 800));
        var template = window.GetVisualDescendants().OfType<ListBox>().Single().ItemTemplate!; Assert.NotNull(template.Build(null));
        await vm.SelectAsync(DemoStories.Items[0]);
        await vm.NavigateAsync("settings");
        foreach (var label in new[] { "阅读排版与字体", "数据来源与缓存", "内容屏蔽", "关于及第三方说明", "外观" })
        {
            var button = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Content is string t && t == label);
            button?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Measure(new(1280, 800)); window.Arrange(new(0, 0, 1280, 800));
        }
        store.Preferences.Theme = "Dark"; vm.SaveSettings();
        await vm.NavigateAsync("account"); await vm.NavigateAsync("discover"); await vm.NavigateAsync("history");
        await vm.SelectAsync(DemoStories.Items[0]); vm.CommentsMode = true; vm.CommentsMode = false;
        window.Width = 900; window.Height = 640; window.Measure(new(900, 640)); window.Arrange(new(0, 0, 900, 640)); window.Close();
        Assert.True(store.History.Count > 0); Directory.Delete(root, true);
    }
    [AvaloniaFact]
    public async Task RemoteImageDecodesWithoutReadingUiPropertiesOnBackgroundThread()
    {
        var root = Path.Combine(Path.GetTempPath(), "heyread-image-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        using var service = new ImageService(root, new ImageHandler()); var image = new RemoteImage(service, "https://cdn.example/image.png"); var window = new Window { Content = image }; window.Show();
        for (var i = 0; i < 100 && !image.IsImageLoaded; i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(image.IsImageLoaded); window.Close(); Directory.Delete(root, true);
    }
    private sealed class ImageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j7ZkAAAAASUVORK5CYII=")) });
    }
    private sealed class NoTranslation : ITranslationService { public Task<string[]> TranslateAsync(string[] text, string target, CancellationToken ct) => Task.FromResult(text); }
}

public sealed class MemorySessionStore : ISessionStore { private Session? _session; public Session? Load() => _session; public void Save(Session? session) => _session = session; }
