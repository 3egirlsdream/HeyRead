using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using HeyRead.Core;
using HeyRead.Desktop.Controls;
using HeyRead.Desktop.Platform;
using static HeyRead.Desktop.Controls.Ui;

namespace HeyRead.Desktop.Views;
public sealed partial class MainWindow
{
    private readonly Grid _readerFrame = new() { RowDefinitions = new("*,64") };
    private readonly ContentControl _readerFooter = new();
    private object? _readerContentIdentity;
    private bool _readerMounted;
    private double ReaderWidth => _narrow ? Math.Max(1, Bounds.Width - 210) : Math.Max(1, Bounds.Width - 211 - _vm.Prefs.ListWidth);
    private bool WideReader => ReaderWidth >= 700;
    private bool HeyboxActions => _vm.Article?.Story.Source == Source.HEYBOX || _vm.VisualFixture;
    private void RenderReader()
    {
        if (_vm.Article is not { } article)
        {
            _readerScroll.Content = null; _readerContentIdentity = null; _detail.Content = EmptyReader(); return;
        }
        var story = article.Story;
        if (_renderedKey != story.Key)
        {
            var history = _vm.Store.History.FirstOrDefault(x => x.Story.Key == story.Key);
            _articleOffset = _vm.CurrentViewport.SelectedStory?.Key == story.Key ? _vm.CurrentViewport.ArticleOffset : history?.ArticleOffset ?? 0; _commentOffset = _vm.CurrentViewport.SelectedStory?.Key == story.Key ? _vm.CurrentViewport.CommentOffset : history?.CommentOffset ?? 0; _renderedKey = story.Key;
        }
        if (!_readerMounted)
        {
            Detach(_readerScroll); _readerFrame.Children.Add(_readerScroll);
            Grid.SetRow(_readerFooter, 1); _readerFrame.Children.Add(_readerFooter); _readerMounted = true;
            _readerScroll.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        }
        var identity = (story.Key, story.Blocks, article.Followed, _vm.IsArticleBlocked, _vm.DetailLoading, _vm.CommentsMode, _vm.CommentsRevision, _vm.CommentsLoading, _vm.CommentsError, _vm.DetailError, _vm.Translated, _vm.Translating, _vm.TranslationMode, _vm.TranslationState, _vm.TranslationError, _vm.Prefs.BodySize, _vm.Prefs.LineHeight, _vm.Prefs.ParagraphSpacing, _vm.Prefs.BodyFont, _vm.Prefs.NoImages, WideReader, _vm.Prefs.Language);
        if (!Equals(_readerContentIdentity, identity))
        {
            _readerContentIdentity = identity;
            _readerScroll.Content = _vm.IsArticleBlocked ? new Border { Child = Group(Text(T("此内容已被屏蔽"), 20, true), AsyncButton(T("临时显示内容"), async () => { _vm.Reveal = true; await _vm.RetryDetailAsync(); })), Margin = new(24) } : _vm.CommentsMode ? BuildComments() : BuildArticle(article);
            var offset = _vm.CommentsMode ? _commentOffset : _articleOffset;
            Dispatcher.UIThread.Post(() => { if (_renderedKey == story.Key) _readerScroll.Offset = new(0, offset); }, DispatcherPriority.Loaded);
        }
        var editorKey = _vm.CommentsMode ? (_vm.Api.Session?.UserId ?? "") + ":" + story.Key + ":root" : null;
        if (editorKey == null || _editorKey != editorKey)
        {
            _editorKey = editorKey;
            _readerFooter.Content = editorKey == null ? BuildReadingActions(article) : BuildCommentActions(article, editorKey);
        }
        _readerFooter.IsEnabled = !_vm.IsArticleBlocked;
        _detail.Content = _readerFrame;
    }

    private string? _editorKey;
    private readonly HashSet<string> _pendingPosts = [];
    private Control BuildCommentActions(Article article, string key)
    {
        var account = _vm.Api.Session?.UserId;
        var editor = new TextBox { PlaceholderText = T("写下你的想法…"), MaxLength = 10000,
            Text = _vm.Store.Drafts.GetValueOrDefault(key, ""), VerticalContentAlignment = VerticalAlignment.Center };
        if (account != null)
            editor.PropertyChanged += (_, change) =>
            {
                if (change.Property == TextBox.TextProperty) _vm.Store.SetDraft(key, editor.Text ?? "");
            };

        var send = AsyncButton(T("发送"), async () =>
        {
            if (_vm.Api.Session == null)
            {
                await LoginDialog();
                if (_vm.Api.Session != null && _vm.Article?.Story.Key == article.Story.Key) SwitchComments(true);
                return;
            }
            if (account != _vm.Api.Session.UserId || _vm.Article?.Story.Key != article.Story.Key) return;
            var draft = editor.Text ?? "";
            if (string.IsNullOrWhiteSpace(draft)) { editor.Focus(); return; }
            if (!_pendingPosts.Add(key)) return;
            editor.IsReadOnly = true;
            try
            {
                await _vm.Api.SocialAsync(article.Story, "comment", true, draft, _vm.Article.AuthCode);
                if (_vm.Store.Drafts.GetValueOrDefault(key) == draft)
                {
                    _vm.Store.SetDraft(key, "");
                    editor.Text = "";
                }
                _vm.Toast = T("发送成功");
                if (_editorKey == key && _vm.Article?.Story.Key == article.Story.Key)
                {
                    await _vm.RetryDetailAsync();
                    if (_vm.Article?.Story.Key == article.Story.Key && _vm.Api.Session?.UserId == account) SwitchComments(true);
                }
            }
            catch (Exception e) { await Notice(T("发送失败，草稿已保留：") + T(e.Message)); }
            finally { _pendingPosts.Remove(key); editor.IsReadOnly = false; }
        }, "primary");
        if (account == null)
        {
            editor.IsReadOnly = true;
            editor.PlaceholderText = T("登录小黑盒");
        }
        return Brush(new Border { Child = Columns("*,12,Auto", editor, new Border(), send), Padding = new(20, 12), BorderThickness = new(0, 1, 0, 0) }, Border.BorderBrushProperty, "Line");
    }
    private Control BuildReadingActions(Article article)
    {
        var actions = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 10, Margin = new(20, 12, 18, 14) };
        var left = Row(10);
        if (HeyboxActions)
        {
            var likes = article.LikeCount is { } count ? " " + count : "";
            left.Children.Add(ActionButton("heart", T(article.Liked == true ? "已喜欢" : "喜欢") + likes, () => Social("like")));
            if (!WideReader) left.Children.Add(ActionButton("comment", T("评论") + Counter(article), () => { SwitchComments(true); return Task.CompletedTask; }));
            left.Children.Add(ActionButton("bookmark", T(article.Favorited == true ? "已收藏" : "收藏"), () => Social("favorite")));
        }
        var original = ActionButton("open", T("查看原文"), () => { OpenUrl(article.Story.Url); return Task.CompletedTask; });
        var menu = new ContextMenu(); var share = new MenuItem { Header = T("分享") }; share.Click += (_, _) => Native.Share(article.Story.Url ?? article.Story.Title); menu.Items.Add(share); original.ContextMenu = menu; left.Children.Add(original);
        if (!WideReader && HeyboxActions)
        {
            var spread = new Grid { ColumnDefinitions = new("*,*,*,*"), ColumnSpacing = 16 };
            var buttons = left.Children.ToArray(); left.Children.Clear();
            for (var i = 0; i < buttons.Length; i++) { Grid.SetColumn(buttons[i], i); buttons[i].HorizontalAlignment = HorizontalAlignment.Stretch; spread.Children.Add(buttons[i]); }
            Grid.SetColumnSpan(spread, 3); actions.Children.Add(spread);
        }
        else actions.Children.Add(left);
        if (WideReader)
        {
            var right = Row(10);
            if (HeyboxActions)
            {
                var text = Button(T("正文"), () => SwitchComments(false), !_vm.CommentsMode ? "primary" : "flat");
                var comments = Button(T("评论") + Counter(article), () => SwitchComments(true), _vm.CommentsMode ? "primary" : "flat");
                text.Height = comments.Height = 30;
                right.Children.Add(Brush(new Border { Child = Row(3, text, comments), Padding = new(3), CornerRadius = new(9) }, Border.BackgroundProperty, "Subtle"));
            }
            right.Children.Add(Row(1, Button("A−", () => ChangeBodySize(-1), "flat"), Button("A+", () => ChangeBodySize(1), "flat")));
            Grid.SetColumn(right, 2); actions.Children.Add(right);
        }
        else if (!HeyboxActions) { var shareButton = ActionButton("share", T("分享"), () => { Native.Share(article.Story.Url ?? article.Story.Title); return Task.CompletedTask; }); Grid.SetColumn(shareButton, 2); actions.Children.Add(shareButton); }
        return Brush(new Border { Child = actions, BorderThickness = new(0, 1, 0, 0) }, Border.BorderBrushProperty, "Line");
    }
    private string Counter(Article article) => (article.CommentCount ?? article.Story.CommentCount) is { } count ? " " + count : _vm.Comments.Count > 0 ? " " + _vm.Comments.Count : "";
    private Button ActionButton(string icon, string label, Func<Task> action)
    {
        var button = AsyncButton("", action); button.Content = Row(8, Icons.Get(icon, 20), Text(label, WideReader ? 14 : _vm.Prefs.Language is "en" or "fr" ? 13 : 16)); button.Height = 36; button.Padding = new(13, 5); return button;
    }
    private void ChangeBodySize(int delta)
    {
        var old = _vm.Prefs.BodySize; var size = Math.Clamp(old + delta, 14, 26);
        _vm.Prefs.BodySize = size; _vm.Prefs.LineHeight = size * Math.Clamp(_vm.Prefs.LineHeight / old, 1.2, 2.4); _vm.SaveSettings();
    }
    private Control BuildArticle(Article article)
    {
        var story = article.Story;
        var stack = Stack(0); stack.MaxWidth = 920; stack.Margin = new(20, WideReader ? 14 : 22, 18, 28);
        var title = Text(story.Title, 26, true); title.Margin = new(WideReader ? 0 : 6, 0, 0, 12); stack.Children.Add(title);
        var authorButton = Button(story.Author, () => { if (story.Profile?.Id is { Length: > 0 } id) Run(() => _vm.NavigateAsync("destination", new("author", id, story.Author))); }, "flat"); authorButton.Padding = new(0); authorButton.FontWeight = FontWeight.SemiBold; authorButton.FontSize = 15;
        var metadata = story.Date + (story.ReadMinutes is { } minutes ? " · " + minutes + " " + T("分钟") : "");
        var row = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new(WideReader ? 0 : 6, 0, 0, WideReader ? 8 : 16) };
        row.Children.Add(Row(12, Avatar(story.Profile?.Avatar, 36), authorButton, Text("· " + metadata, 15, muted: true)));
        if (HeyboxActions)
        {
            var follow = AsyncButton(T(article.Followed == true ? "已关注" : "关注"), () => Social("follow")); follow.Classes.Add("accentOutline"); follow.Height = 30; follow.MinHeight = 30; follow.MinWidth = 66; follow.Padding = new(10, 3); Grid.SetColumn(follow, 1); row.Children.Add(follow);
        }
        stack.Children.Add(row);
        if (_vm.DetailLoading) { var progress = new ProgressBar { IsIndeterminate = true, Height = 2, Margin = new(0, 0, 0, 12) }; stack.Children.Add(progress); }
        if (_vm.DetailError.Length > 0) stack.Children.Add(Group(Text(T(_vm.DetailError)), AsyncButton(T("重试"), _vm.RetryDetailAsync), Button(T("官方验证入口"), Verify)));
        if (story.Notice != null) { var notice = Text(T(story.Notice), 12, muted: true); notice.Margin = new(0, 0, 0, 12); stack.Children.Add(notice); }
        var blocks = story.Blocks ?? ContentParser.Html(story.Body.Length > 0 ? story.Body : story.Excerpt);
        var hero = story.HeroImage ?? story.Image;
        var gallery = ArticleImages(blocks).Prepend(hero).Where(url => !string.IsNullOrEmpty(url)).Cast<string>().Distinct().ToArray();
        if (!_vm.Prefs.NoImages && hero != null)
        {
            var picture = Picture(hero, WideReader ? 218 : 260); picture.Margin = new(0, 0, 0, _vm.VisualFixture && WideReader ? 6 : 18); stack.Children.Add(picture);
            picture.Tapped += (_, _) => { if (!hero.StartsWith("design:")) Run(() => ImageViewer(gallery, 0)); };
            if (_vm.VisualFixture && WideReader) { var caption = Text(T("苇名城远景，雾气在山谷间流动，战斗的节奏也在此处逐渐展开。"), 12, muted: true); caption.Margin = new(0, 0, 0, 8); stack.Children.Add(caption); }
        }
        if (story.Source == Source.STEAM) stack.Children.Add(BuildTranslationBar());
        if (story.Source == Source.STEAM && _vm.TranslationMode == "translated" && _vm.TranslatedBlocks != null) blocks = _vm.TranslatedBlocks;
        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];
            if (block.Kind == BlockKind.Image && block.Url == hero) continue;
            if (_vm.VisualFixture && !WideReader && block.Kind is BlockKind.Game or BlockKind.Quote) continue;
            if (story.Source == Source.STEAM && _vm.TranslationMode == "split" && _vm.TranslatedBlocks != null && block.Kind is BlockKind.Paragraph or BlockKind.Heading or BlockKind.ListItem or BlockKind.Quote)
            {
                var translated = _vm.TranslatedBlocks[index];
                stack.Children.Add(ReaderWidth >= 700 ? Columns("*,16,*", RenderBlock(block, gallery), new Border(), RenderBlock(translated, gallery)) : Stack(6, Text(T("原文"), 12, muted: true), RenderBlock(block, gallery), Text(T("译文"), 12, muted: true), RenderBlock(translated, gallery)));
            }
            else stack.Children.Add(RenderBlock(block, gallery));
        }
        var labels = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new(0, 12, 0, 0) };
        foreach (var label in story.Sections ?? []) labels.Children.Add(Button(label.Name, () => Run(() => _vm.NavigateAsync("destination", new("section", label.Id, label.Name))), "flat"));
        foreach (var label in story.Topics ?? []) labels.Children.Add(Button("#" + label.Name, () => Run(() => _vm.NavigateAsync("destination", new("topic", label.Id, label.Name))), "flat"));
        if (labels.Children.Count > 0) stack.Children.Add(labels);
        return stack;
    }
    private Control BuildTranslationBar()
    {
        var row = Row(6); var panel = Stack(8); panel.Margin = new(0, 0, 0, 16);
        if (_vm.Translating) { row.Children.Add(Text(T("正在准备翻译资源…"), 13, muted: true)); row.Children.Add(Button(T("取消"), _vm.CancelTranslation)); }
        else row.Children.Add(AsyncButton(T(_vm.TranslationState == "failed" ? "重试翻译" : "翻译为中文"), _vm.TranslateAsync));
        if (_vm.TranslatedBlocks != null) foreach (var (value, label) in new[] { ("original", "原文"), ("translated", "译文"), ("split", "对照") }) row.Children.Add(Button(T(label), () => _vm.TranslationMode = value, _vm.TranslationMode == value ? "primary" : "flat"));
        panel.Children.Add(row);
        if (_vm.TranslationState == "failed") panel.Children.Add(Text(T("翻译失败：") + T(_vm.TranslationError), 13));
        if (_vm.Translating) panel.Children.Add(new ProgressBar { Height = 2, IsIndeterminate = true });
        return panel;
    }
    private static IEnumerable<string> ArticleImages(IReadOnlyList<ArticleBlock> blocks)
    {
        foreach (var block in blocks) { if (block.Kind == BlockKind.Image && block.Url != null) yield return block.Url; if (block.Children != null) foreach (var url in ArticleImages(block.Children)) yield return url; }
    }
}
