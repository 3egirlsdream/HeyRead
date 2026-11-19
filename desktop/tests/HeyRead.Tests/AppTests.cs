using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using System.Text.Json;
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
    public async Task SearchControlsAndAccountRowsKeepTheirMeasuredSizesAcrossViewports()
    {
        var root = Directory.CreateTempSubdirectory("heyread-geometry-");
        var store = new LibraryStore(root.FullName);
        using var api = new HeyReadApi(new FakeHandler("""{"status":"ok","result":{"links":[]}}"""));
        using var vm = new MainViewModel(api, new(api), store, new MemorySessionStore(), new NoTranslation());
        var window = new MainWindow(vm);
        try
        {
            window.Show();
            foreach (var size in new[] { new Size(1280, 800), new Size(900, 640), new Size(428, 640) })
            {
                window.Width = size.Width;
                window.Height = size.Height;
                await vm.NavigateAsync("search");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.Measure(size);
                window.Arrange(new Rect(size));
                window.UpdateLayout();
                Assert.Equal(size, window.Bounds.Size);
                var field = window.GetVisualDescendants().OfType<TextBox>().Single(control => control.MaxWidth == 668);
                Assert.Equal(40, field.Bounds.Height);
                Assert.Equal(Math.Min(668, size.Width - 70), field.Bounds.Width);
                var tabs = window.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("sectionTab")).ToList();
                Assert.Equal(3, tabs.Count);
                Assert.All(tabs, tab => Assert.Equal(new Size(77, 30), tab.Bounds.Size));

                await vm.NavigateAsync("account");
                window.UpdateLayout();
                var group = window.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("accountLinks"));
                Assert.Equal(42, group.Bounds.Height);
                Assert.Equal(40, Assert.Single(group.GetVisualDescendants().OfType<Button>()).Bounds.Height);
            }
        }
        finally
        {
            window.Close();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            root.Delete(true);
        }
    }

    [AvaloniaFact]
    public async Task CommentComposerRestoresDraftAndPostsToTheSelectedStory()
    {
        var root = Directory.CreateTempSubdirectory("heyread-composer-");
        var store = new LibraryStore(root.FullName);
        store.Preferences.NoImages = true;
        var publicFeed = new FakeHandler("""{"status":"ok","result":{"links":[]}}""");
        var account = new FakeHandler("""{"status":"ok","result":{"link":{"content":"<p>Article</p>"},"comment":{"comments":[]}}}""");
        using var api = new HeyReadApi(publicFeed, account);
        api.SetSession(new("pkey=test", "42", "Fixture", ""));
        using var vm = new MainViewModel(api, new(api), store, new MemorySessionStore(), new NoTranslation());
        var window = new MainWindow(vm);
        try
        {
            window.Show();
            await vm.SelectAsync(new Story("55", Source.HEYBOX, "Draft article", "Summary"));
            Window OpenComposer()
            {
                window.UpdateLayout();
                var compose = window.GetVisualDescendants().OfType<Button>().Single(button => Avalonia.Automation.AutomationProperties.GetName(button) == "写评论");
                compose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                return Assert.Single(window.OwnedWindows);
            }
            TextBox Editor(Window dialog) => dialog.GetVisualDescendants().OfType<TextBox>().Single(box => box.MaxLength == 10000);
            var first = OpenComposer();
            Editor(first).Text = "saved thought";
            first.Close();
            var second = OpenComposer();
            Assert.Equal("saved thought", Editor(second).Text);
            var send = second.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "发送"));
            send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(window, () => !second.IsVisible);
            var request = Assert.Single(account.Requests, r => r.Uri.AbsolutePath.EndsWith("/comment/create"));
            Assert.Contains("link_id=55", request.Form);
            Assert.Contains("text=saved", request.Form);
            Assert.Empty(new LibraryStore(root.FullName).Drafts);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); root.Delete(true); }
    }

    [AvaloniaFact]
    public async Task ScrollingAppendsFeedAndInlineCommentsWithoutReplacingTheArticle()
    {
        var root = Directory.CreateTempSubdirectory("heyread-scroll-");
        var store = new LibraryStore(root.FullName);
        store.Preferences.NoImages = true;
        var feed = new ScrollFixture();
        using var api = new HeyReadApi(feed);
        using var vm = new MainViewModel(api, new(api), store, new MemorySessionStore(), new NoTranslation());
        var window = new MainWindow(vm);
        try
        {
            window.Show();
            await WaitFor(window, () => vm.Stories.Count == 20);
            var list = window.GetVisualDescendants().OfType<ListBox>().Single();
            var feedScroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.Equal(new[] { 0 }, feed.FeedOffsets);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "加载更多"));
            var dock = Assert.Single(window.GetVisualDescendants().OfType<LiquidGlassAvaloniaUI.LiquidGlassSurface>());
            var tabs = dock.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("dockItem")).ToArray();
            Assert.Equal(6, tabs.Length);
            foreach (var button in tabs)
            {
                var content = Assert.IsType<StackPanel>(button.Content);
                var glyph = content.Children[0];
                var glyphCenter = glyph.TranslatePoint(new Rect(glyph.Bounds.Size).Center, button)!.Value;
                var contentTop = content.TranslatePoint(default, dock)!.Value.Y;
                Assert.Equal(50, button.Bounds.Height);
                Assert.True(button.Bounds.Width > button.Bounds.Height);
                Assert.Equal(new Size(20, 20), glyph.Bounds.Size);
                Assert.InRange(glyphCenter.X, button.Bounds.Width / 2 - .5, button.Bounds.Width / 2 + .5);
                Assert.InRange(contentTop, 9.5, 10.5);
                Assert.InRange(dock.Bounds.Height - contentTop - content.Bounds.Height, 9.5, 10.5);
            }
            Assert.Null(vm.Article);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button => Avalonia.Automation.AutomationProperties.GetName(button) == "关闭详情");

            feedScroll.ScrollToEnd();
            await WaitFor(window, () => vm.Stories.Count == 40);
            Assert.Equal(new[] { 0, 10 }, feed.FeedOffsets);
            Assert.True(feedScroll.Offset.Y > 0);

            var compactWidth = window.Bounds.Width;
            Assert.InRange(compactWidth, 360, 480);
            var windowPosition = window.Position;
            var feedOrigin = list.TranslatePoint(default, window);
            var feedWidth = list.Bounds.Width;
            var feedOffset = feedScroll.Offset.Y;
            var dockOrigin = dock.TranslatePoint(default, window);
            var dockWidth = dock.Bounds.Width;

            void AssertFeedStaysInPlace()
            {
                Assert.Equal(windowPosition, window.Position);
                Assert.Equal(feedOrigin, list.TranslatePoint(default, window));
                Assert.Equal(feedWidth, list.Bounds.Width);
                Assert.Equal(feedOffset, feedScroll.Offset.Y, precision: 1);
                Assert.Equal(dockOrigin, dock.TranslatePoint(default, window));
                Assert.Equal(dockWidth, dock.Bounds.Width);
            }

            list.SelectedItem = vm.Stories[0];
            await WaitFor(window, () => vm.Article != null && !vm.DetailLoading && window.Bounds.Width >= compactWidth + 360);
            AssertFeedStaysInPlace();
            var title = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Fixture article 1" && t.FontSize == 24);
            var reader = title.FindAncestorOfType<ScrollViewer>()!;
            var commentHeader = reader.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "评论");
            Assert.True(commentHeader.TranslatePoint(default, reader)!.Value.Y > reader.Viewport.Height);
            Assert.Equal(new[] { 1 }, feed.CommentPages);
            Assert.Single(reader.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("commentBranch"));

            reader.ScrollToEnd();
            await WaitFor(window, () => vm.Comments.Count == 2);
            Assert.Equal(new[] { 1, 2 }, feed.CommentPages);
            Assert.Same(reader, title.FindAncestorOfType<ScrollViewer>());
            Assert.True(reader.Offset.Y > 0);
            Assert.Equal(2, reader.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("commentRoot")));
            Assert.DoesNotContain(reader.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "加载更多"));

            reader.Offset = default;
            var jump = window.GetVisualDescendants().OfType<Button>().Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "评论 2");
            jump.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(window, () => reader.Offset.Y > 0);
            Assert.InRange(commentHeader.TranslatePoint(default, reader)!.Value.Y, 0, reader.Viewport.Height);
            Assert.Same(reader, title.FindAncestorOfType<ScrollViewer>());

            var moreReplies = reader.GetVisualDescendants().OfType<Button>().Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "查看更多回复");
            moreReplies.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(window, () => feed.ReplyCursors.Count == 2);
            Assert.Empty(window.OwnedWindows);
            Assert.Equal(new string?[] { null, "next" }, feed.ReplyCursors);
            Assert.Equal(3, reader.GetVisualDescendants().Count(c => c.Classes.Contains("commentReply")));
            var collapse = reader.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "收起回复"));
            collapse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(window, () => !reader.GetVisualDescendants().Any(c => c.Classes.Contains("commentReply")));
            var expand = reader.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "展开 3 条回复"));
            expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(window, () => reader.GetVisualDescendants().Count(c => c.Classes.Contains("commentReply")) == 3);
            Assert.Equal(2, feed.ReplyCursors.Count);

            window.Width = 900;
            await WaitFor(window, () => window.Bounds.Width == 900 && reader.IsEffectivelyVisible);
            AssertFeedStaysInPlace();
            Assert.True(list.IsEffectivelyVisible);
            Assert.True(list.TranslatePoint(default, window)!.Value.X + list.Bounds.Width < reader.TranslatePoint(default, window)!.Value.X);
            var close = window.GetVisualDescendants().OfType<Button>().Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "关闭详情");
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(window, () => vm.Article == null && !window.GetVisualDescendants().Contains(reader) && window.Bounds.Width == compactWidth);
            AssertFeedStaysInPlace();
            Assert.Null(list.SelectedItem);
            list.SelectedItem = vm.Stories[0];
            await WaitFor(window, () => window.GetVisualDescendants().Contains(reader) && reader.IsEffectivelyVisible);
            Assert.Contains(reader.GetVisualDescendants().OfType<TextBlock>(), label => label.Text == title.Text);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); root.Delete(true); }
    }

    [AvaloniaFact]
    public async Task DiscoverHasNoHeaderAndRefreshesFromPullAndNavigation()
    {
        var root = Directory.CreateTempSubdirectory("heyread-pull-");
        var store = new LibraryStore(root.FullName);
        store.Preferences.NoImages = true;
        var feed = new ScrollFixture();
        using var api = new HeyReadApi(feed);
        using var vm = new MainViewModel(api, new(api), store, new MemorySessionStore(), new NoTranslation());
        var window = new MainWindow(vm);
        try
        {
            window.Show();
            await WaitFor(window, () => vm.Stories.Count == 20);
            var list = window.GetVisualDescendants().OfType<ListBox>().Single();
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "推荐"));
            Assert.Equal("PingFang SC", window.FontFamily.Name);
            Assert.Equal(Avalonia.Media.TextRenderingMode.Antialias, Avalonia.Media.TextOptions.GetTextRenderingMode(window));
            Assert.Equal(Avalonia.Media.EdgeMode.Antialias, Avalonia.Media.RenderOptions.GetEdgeMode(window));
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBox>(), box => box.IsEffectivelyVisible);
            var point = list.TranslatePoint(new Point(list.Bounds.Width / 2, 64), window)!.Value;
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
            scroll.Offset = new Vector(0, 120);
            window.MouseWheel(point, new Vector(0, 12), Avalonia.Input.RawInputModifiers.None);
            await WaitFor(window, () => scroll.Offset.Y == 0);
            for (var i = 0; i < 3; i++)
                window.MouseWheel(point, new Vector(0, 1), Avalonia.Input.RawInputModifiers.None);
            await Task.Delay(250, TestContext.Current.CancellationToken);
            Assert.Equal(new[] { 0 }, feed.FeedOffsets);

            window.MouseWheel(point, new Vector(0, 1), Avalonia.Input.RawInputModifiers.None);
            await Task.Delay(250, TestContext.Current.CancellationToken);
            Assert.Equal(new[] { 0 }, feed.FeedOffsets);
            for (var i = 0; i < 3; i++)
                window.MouseWheel(point, new Vector(0, 1), Avalonia.Input.RawInputModifiers.None);
            await WaitFor(window, () => feed.FeedOffsets.Count == 2);
            Assert.Equal(new[] { 0, 0 }, feed.FeedOffsets);

            Button DockTab(string label) => window.GetVisualDescendants().OfType<Button>().Single(button =>
                button.Classes.Contains("dockItem") && Avalonia.Automation.AutomationProperties.GetName(button) == label);
            Color ButtonFill(Button button) => Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color;
            Color DockBrush(string key) => Assert.IsAssignableFrom<ISolidColorBrush>(window.FindResource(window.ActualThemeVariant, key)).Color;
            Point DockCenter(Button button) => button.TranslatePoint(new Rect(button.Bounds.Size).Center, window)!.Value;
            var discover = DockTab("发现");
            var search = DockTab("搜索");
            window.MouseMove(DockCenter(search), Avalonia.Input.RawInputModifiers.None);
            Assert.Equal(DockBrush("DockHover"), ButtonFill(search));
            Assert.Equal(DockBrush("DockSelection"), ButtonFill(discover));
            window.MouseMove(DockCenter(discover), Avalonia.Input.RawInputModifiers.None);
            Assert.Equal(DockBrush("DockSelection"), ButtonFill(discover));
            Assert.Equal(Colors.Transparent, ButtonFill(search));
            search.Focus(NavigationMethod.Tab);
            Assert.Equal(DockBrush("DockFocus"), Assert.IsAssignableFrom<ISolidColorBrush>(search.BorderBrush).Color);

            discover.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(window, () => feed.FeedOffsets.Count == 3);
            search.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(window, () => vm.Page == "search" && window.GetVisualDescendants().OfType<TextBox>().Any(box => box.IsEffectivelyVisible));
            DockTab("发现").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(window, () => vm.Page == "discover" && feed.FeedOffsets.Count == 4 && !vm.Loading);
            Assert.Equal(new[] { 0, 0, 0, 0 }, feed.FeedOffsets);
            Assert.Null(vm.Article);
            var more = DockTab("更多");
            more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(more.ContextMenu?.IsOpen);
            Assert.Contains(more.ContextMenu!.Items.OfType<MenuItem>(), item => Equals(item.Header, "刷新"));
            more.ContextMenu.Close();
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); root.Delete(true); }
    }

    private static async Task WaitFor(Window window, Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            if (condition()) return;
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.True(condition());
    }

    [AvaloniaFact]
    public async Task ArticleAndWrappedRepliesKeepInlineEmojiInsideTheirTextLayout()
    {
        var root = Directory.CreateTempSubdirectory("heyread-reply-layout-");
        var store = new LibraryStore(root.FullName);
        var emojiUrl = "https://imgheybox.max-c.com/heybox/emoji/cube_14.png";
        var emojiCache = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(emojiUrl)));
        File.WriteAllBytes(Path.Combine(root.FullName, "cache", emojiCache),
            Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j7ZkAAAAASUVORK5CYII="));
        var replyBody = string.Concat(Enumerable.Repeat("这里有很长的中英文混合回复 [cube_喜欢] and some extra text. ", 5));
        var articleBody = "<p>" + string.Concat(Enumerable.Repeat("正文需要稳定行距 [cube_喜欢] and inline text. ", 5)) + "<br><br>第二段。</p>";
        var body = JsonSerializer.Serialize(new
        {
            status = "ok",
            result = new
            {
                links = Array.Empty<object>(),
                link = new { content = articleBody },
                comment = new
                {
                    comments = new[]
                    {
                        new
                        {
                            comment = new[]
                            {
                                new { comment_id = "root", text = "[cube_喜欢]", up = 0, user = new { userid = "member", nickname = "Root" } },
                                new { comment_id = "first", text = replyBody, up = 128, user = new { userid = "writer", nickname = "混排Writer" } },
                                new { comment_id = "next", text = "下一条回复", up = 0, user = new { userid = "other", nickname = "下一位" } }
                            }
                        }
                    }
                }
            }
        });
        using var api = new HeyReadApi(new FakeHandler(body));
        using var vm = new MainViewModel(api, new(api), store, new MemorySessionStore(), new NoTranslation());
        var window = new MainWindow(vm);
        try
        {
            window.Show();
            await vm.SelectAsync(new Story("reply-layout", Source.HEYBOX, "正文排版 [cube_喜欢]", "Summary"));
            window.Width = 900;
            var rows = Array.Empty<Grid>();
            await WaitFor(window, () =>
            {
                rows = window.GetVisualDescendants().OfType<Grid>().Where(row => row.Classes.Contains("commentReply")).ToArray();
                return rows.Length == 2 && rows[0].Bounds.Height > 0;
            });
            var title = window.GetVisualDescendants().OfType<TextBlock>().Single(label => label.FontSize == 24);
            Assert.Single(title.Inlines!.OfType<Avalonia.Controls.Documents.InlineUIContainer>());
            var paragraphs = window.GetVisualDescendants().OfType<SelectableTextBlock>().Where(label => label.FontSize == 16).ToArray();
            Assert.Equal(2, paragraphs.Length);
            var paragraph = paragraphs[0];
            Assert.True(paragraph.TextLayout.TextLines.Count > 1);
            Assert.Equal(27, paragraph.LineHeight);
            Assert.Equal(14, paragraph.Margin.Bottom);
            foreach (var inline in paragraph.Inlines!.OfType<Avalonia.Controls.Documents.InlineUIContainer>())
            {
                Assert.Equal(BaselineAlignment.Center, inline.BaselineAlignment);
                Assert.Equal(paragraph.FontSize, inline.Child.Bounds.Height);
                var imageTop = inline.Child.TranslatePoint(default, paragraph)!.Value.Y;
                Assert.InRange(imageTop, 0, paragraph.Bounds.Height - inline.Child.Bounds.Height + .5);
            }
            var first = rows[0];
            var reader = paragraph.FindAncestorOfType<ScrollViewer>()!;
            Assert.Same(reader, first.FindAncestorOfType<ScrollViewer>());
            var targetOffset = Math.Max(0, first.TranslatePoint(default, reader)!.Value.Y - 72);
            reader.Offset += new Vector(0, targetOffset);
            await WaitFor(window, () => first.TranslatePoint(default, reader)!.Value.Y < reader.Viewport.Height - 24);
            var text = Assert.Single(first.GetVisualDescendants().OfType<SelectableTextBlock>());
            Assert.Equal("混排Writer", Assert.IsType<Avalonia.Controls.Documents.Run>(text.Inlines![0]).Text);
            Assert.True(text.TextLayout.TextLines.Count > 1);
            Assert.True(double.IsNaN(text.LineHeight));
            var actions = first.Children[1];
            var textOrigin = text.TranslatePoint(default, window)!.Value;
            var actionsOrigin = actions.TranslatePoint(default, window)!.Value;
            var nextOrigin = rows[1].TranslatePoint(default, window)!.Value;
            Assert.True(textOrigin.X + text.Bounds.Width <= actionsOrigin.X);
            Assert.True(textOrigin.Y + text.Bounds.Height <= nextOrigin.Y);
            Assert.Equal(actionsOrigin.X, rows[1].Children[1].TranslatePoint(default, window)!.Value.X);
            var emoji = text.Inlines.OfType<Avalonia.Controls.Documents.InlineUIContainer>().ToArray();
            Assert.Equal(5, emoji.Length);
            foreach (var inline in emoji)
            {
                var child = inline.Child;
                var origin = child.TranslatePoint(default, text)!.Value;
                Assert.InRange(origin.Y, 0, text.Bounds.Height - child.Bounds.Height + .5);
                Assert.InRange(origin.X + child.Bounds.Width, 0, text.Bounds.Width + .5);
            }
            var authorHit = textOrigin + new Vector(3, text.TextLayout.TextLines[0].Height / 2);
            window.MouseDown(authorHit, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseUp(authorHit, MouseButton.Left, RawInputModifiers.None);
            await WaitFor(window, () => vm.Target?.Id == "writer");
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); root.Delete(true); }
    }

    private sealed class ScrollFixture : HttpMessageHandler
    {
        public List<int> FeedOffsets { get; } = [];
        public List<int> CommentPages { get; } = [];
        public List<string?> ReplyCursors { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            object result;
            switch (uri.AbsolutePath)
            {
                case "/bbs/app/feeds":
                    var offset = int.Parse(query["offset"]!);
                    FeedOffsets.Add(offset);
                    var links = offset < 20
                        ? Enumerable.Range(1 + offset * 2, 20).Select(id => new { linkid = id.ToString(), title = $"Fixture article {id}" }).ToArray()
                        : [];
                    result = new { links };
                    break;
                case "/bbs/app/link/tree":
                    var page = int.Parse(query["page"]!);
                    CommentPages.Add(page);
                    var id = page.ToString();
                    var main = new { comment_id = id, text = $"Comment {page}", user = new { nickname = "Reader" }, child_num = page == 1 ? 3 : 0 };
                    var replies = page == 1 ? new[] { new { comment_id = "reply", text = "Nested reply", user = new { nickname = "Writer" } } } : [];
                    result = new { comment = new { comments = new[] { new { comment = main, sub_comments = replies } },
                        comment_config = new { has_more_floors = page == 1 } } };
                    break;
                case "/bbs/app/comment/sub/comments":
                    var cursor = query["lastval"];
                    ReplyCursors.Add(cursor);
                    var replyIds = cursor == null ? new[] { "reply", "reply-2" } : new[] { "reply-3" };
                    result = new
                    {
                        comments = replyIds.Select(replyId => new { comment_id = replyId, text = replyId, user = new { nickname = "Writer" } }),
                        has_more = cursor == null,
                        lastval = cursor == null ? "next" : null
                    };
                    break;
                case "/bbs/app/link/web/view":
                    result = new { link = new { content = string.Concat(Enumerable.Repeat("<p>A sample paragraph of the article.</p>", 48)) } };
                    break;
                default:
                    throw new InvalidOperationException(uri.AbsolutePath);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { status = "ok", result })),
                RequestMessage = request
            });
        }
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
        await vm.SelectAsync(DemoStories.Items[0]);
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
