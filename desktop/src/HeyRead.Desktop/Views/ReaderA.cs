using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using HeyRead.Core;
using HeyRead.Desktop.Controls;
using HeyRead.Desktop.Platform;
using static HeyRead.Desktop.Controls.Ui;

namespace HeyRead.Desktop.Views;
public sealed partial class MainWindow
{
    private readonly Grid _readerFrame = new() { RowDefinitions = new("38,*,52") };
    private readonly ContentControl _readerHeader = new();
    private readonly ContentControl _readerFooter = new();
    private readonly ContentControl _articleContent = new();
    private readonly ContentControl _commentsSection = new();
    private StackPanel? _readerDocument;
    private object? _readerContentIdentity;
    private object? _commentContentIdentity;
    private double? _pendingReaderOffset, _pendingCommentOffset;
    private double ReaderWidth
    {
        get
        {
            var leftWidth = FeedColumnWidth + 2 * PaneInset + PaneGap;
            return Math.Max(1, (ClientSize.Width > leftWidth ? ClientSize.Width : Width) - leftWidth);
        }
    }
    private bool HeyboxActions => _vm.Article?.Story.Source == Source.HEYBOX || _vm.VisualFixture;

    private void RenderReader()
    {
        if (_vm.Article is not { } article)
        {
            _articleContent.Content = _commentsSection.Content = null;
            _readerContentIdentity = _commentContentIdentity = null;
            _renderedKey = null;
            _replyStoryKey = null;
            _replyThreads.Clear();
            _articleOffset = 0;
            _pendingReaderOffset = _pendingCommentOffset = null;
            _readerHeader.Content = _readerFooter.Content = _detail.Content = null;
            return;
        }

        var story = article.Story;
        if (_renderedKey != story.Key)
        {
            var viewport = _vm.CurrentViewport;
            var restoring = viewport.SelectedStory?.Key == story.Key;
            _articleOffset = restoring ? viewport.ArticleOffset : _vm.Store.History.FirstOrDefault(x => x.Story.Key == story.Key)?.ArticleOffset ?? 0;
            _pendingReaderOffset = _articleOffset;
            _pendingCommentOffset = restoring && viewport.CommentsMode && HeyboxActions ? viewport.CommentOffset : null;
            _renderedKey = story.Key;
        }
        if (_readerDocument == null)
        {
            _readerDocument = Stack(0, _articleContent, _commentsSection);
            _readerDocument.Margin = new(28, 16, 28, 24);
            _readerScroll.Content = _readerDocument;
            _readerFrame.Children.Add(_readerHeader);
            Grid.SetRow(_readerScroll, 1);
            _readerFrame.Children.Add(_readerScroll);
            Grid.SetRow(_readerFooter, 2);
            _readerFrame.Children.Add(_readerFooter);
        }

        _readerDocument.MaxWidth = story.Source == Source.STEAM && _vm.TranslationMode == "split" ? 744 : 656;
        var identity = (story, article.Followed, _vm.IsArticleBlocked, _vm.DetailLoading, _vm.DetailError,
            _vm.Translated, _vm.Translating, _vm.TranslationMode, _vm.TranslationState, _vm.TranslationError,
            _vm.Prefs.BodySize, _vm.Prefs.LineHeight, _vm.Prefs.ParagraphSpacing, _vm.Prefs.BodyFont,
            _vm.Prefs.NoImages, ReaderWidth >= 700, _vm.Prefs.Language);
        if (!Equals(_readerContentIdentity, identity))
        {
            _readerContentIdentity = identity;
            _pendingReaderOffset ??= _articleOffset;
            _articleContent.Content = _vm.IsArticleBlocked
                ? Group(Text(T("此内容已被屏蔽"), 18, true), AsyncButton(T("临时显示内容"), async () => { _vm.Reveal = true; await _vm.RetryDetailAsync(); }))
                : BuildArticle(article);
        }

        if (HeyboxActions && !_vm.IsArticleBlocked && !_vm.DetailLoading && _vm.DetailError.Length == 0)
            RenderComments(article);
        else
        {
            _commentsSection.Content = null;
            _commentContentIdentity = null;
        }
        _readerHeader.Content = BuildReaderHeader(story);
        _readerFooter.Content = BuildReadingActions(article);
        _readerFooter.IsEnabled = !_vm.IsArticleBlocked;
        _detail.Content = _readerFrame;
        QueuePagination();
    }

    private void JumpToComments()
    {
        if (_commentsSection.Content == null) return;
        _pendingCommentOffset = 0;
        QueuePagination();
    }

    private void RestoreReaderOffset()
    {
        if ((_pendingReaderOffset == null && _pendingCommentOffset == null) || _readerDocument == null ||
            !_readerScroll.IsEffectivelyVisible || _readerScroll.Viewport.Height <= 0 || _vm.DetailLoading) return;
        var offset = _pendingReaderOffset ?? _articleOffset;
        if (_pendingCommentOffset is { } commentOffset && _commentsSection.Content != null &&
            _commentsSection.TranslatePoint(default, _readerDocument) is { } anchor)
            offset = _readerDocument.Margin.Top + anchor.Y + commentOffset;
        _readerScroll.Offset = new(0, offset);
        if (_readerScroll.Offset.Y + 1 < offset && _commentsSection.Content != null && _vm.MoreComments && _vm.CommentsError.Length == 0)
            return;
        _articleOffset = _readerScroll.Offset.Y;
        _pendingReaderOffset = _pendingCommentOffset = null;
        ScheduleViewportSave();
    }

    private Control BuildReaderHeader(Story story)
    {
        var title = Text(PlainReaderTitle(story.Title), 12);
        title.MaxWidth = Math.Max(110, Math.Min(284, ReaderWidth - 135));
        title.TextWrapping = TextWrapping.NoWrap;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTip.SetTip(title, title.Text);
        var close = Button("", CloseReader, "flat");
        close.Content = Icons.Get("dismiss", 13, "Muted");
        close.Width = close.Height = close.MinHeight = 23;
        close.Padding = default;
        ToolTip.SetTip(close, T("关闭详情"));
        Avalonia.Automation.AutomationProperties.SetName(close, T("关闭详情"));
        var tab = Brush(new Border
        {
            Child = Row(6, Icons.Get("book_open", 15, "Muted"), title, close),
            Padding = new(8, 1, 1, 1), CornerRadius = new(6), HorizontalAlignment = HorizontalAlignment.Left
        }, Border.BackgroundProperty, "Subtle");

        var more = new Button { Content = Icons.Get("more_horizontal", 17), Width = 30, Classes = { "compactAction", "iconOnly", "flat" } };
        ToolTip.SetTip(more, T("更多操作"));
        Avalonia.Automation.AutomationProperties.SetName(more, T("更多操作"));
        var menu = new ContextMenu();
        foreach (var (label, action) in new (string, Action)[]
        {
            ("查看原文", () => OpenUrl(story.Url)),
            ("分享", () => Native.Share(story.Url ?? story.Title)),
            ("减小正文字号", () => ChangeBodySize(-1)),
            ("增大正文字号", () => ChangeBodySize(1))
        })
        {
            var item = new MenuItem { Header = T(label) };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        more.ContextMenu = menu;
        more.Click += (_, _) => menu.Open(more);
        var bar = Columns("*,Auto", tab, more);
        bar.Margin = new(10, 0, 6, 0);
        bar.VerticalAlignment = VerticalAlignment.Center;
        return Brush(new Border { Child = bar, BorderThickness = new(0, 0, 0, 1) }, Border.BorderBrushProperty, "Line");
    }

    private Control BuildReadingActions(Article article)
    {
        var actions = Row(3);
        var iconsOnly = ReaderWidth < 570 || _vm.Prefs.Language is "en" or "fr";
        void Add(Button button)
        {
            button.Classes.Add("flat");
            actions.Children.Add(button);
        }

        if (HeyboxActions)
        {
            var likeLabel = T(article.Liked == true ? "已喜欢" : "喜欢");
            var like = ActionButton("thumb_like", article.LikeCount?.ToString() ?? likeLabel, () => Social("like"), iconsOnly, article.Liked == true);
            ToolTip.SetTip(like, likeLabel);
            Avalonia.Automation.AutomationProperties.SetName(like, likeLabel + (article.LikeCount is { } count ? " " + count : ""));
            Add(like);
            Add(ActionButton("bookmark", T(article.Favorited == true ? "已收藏" : "收藏"), () => Social("favorite"), iconsOnly, article.Favorited == true));
        }
        Add(ActionButton("open", T("查看原文"), () => { OpenUrl(article.Story.Url); return Task.CompletedTask; }, iconsOnly));
        Add(ActionButton("share", T("分享"), () => { Native.Share(article.Story.Url ?? article.Story.Title); return Task.CompletedTask; }, iconsOnly: true));
        if (HeyboxActions)
        {
            var comments = ActionButton("comment", T("评论") + Counter(article), () => { JumpToComments(); return Task.CompletedTask; });
            comments.IsEnabled = _commentsSection.Content != null;
            Add(comments);
        }

        var story = article.Story;
        var author = Text(story.Author, 12, true);
        author.MaxLines = 1;
        author.TextTrimming = TextTrimming.CharacterEllipsis;
        var identity = Columns("Auto,*", Avatar(_vm.Prefs.NoImages ? null : story.Profile?.Avatar, 26), author);
        identity.ColumnSpacing = 7;
        identity.VerticalAlignment = VerticalAlignment.Center;
        author.VerticalAlignment = VerticalAlignment.Center;
        var bar = Columns("*,Auto", identity, actions);
        bar.ColumnSpacing = 8;
        bar.Margin = new(28, 0);
        bar.MaxWidth = 744;
        return Brush(new Border { Child = bar, BorderThickness = new(0, 1, 0, 0) }, Border.BorderBrushProperty, "Line");
    }

    private string Counter(Article article) => (article.CommentCount ?? article.Story.CommentCount) is { } count ? " " + count : _vm.Comments.Count > 0 ? " " + _vm.Comments.Count : "";

    private Button ActionButton(string icon, string label, Func<Task> action, bool iconsOnly = false, bool selected = false)
    {
        var button = AsyncButton("", action, "compactAction");
        var glyph = Icons.Get(icon, 15, selected ? "Accent" : "Ink");
        button.Content = iconsOnly ? glyph : Row(5, glyph, Text(label, 12));
        if (iconsOnly)
        {
            button.Width = 30;
            button.Padding = default;
        }
        button.Classes.Set("selected", selected);
        ToolTip.SetTip(button, label);
        Avalonia.Automation.AutomationProperties.SetName(button, label);
        return button;
    }

    private void ChangeBodySize(int delta)
    {
        var old = _vm.Prefs.BodySize; var size = Math.Clamp(old + delta, 14, 26);
        _vm.Prefs.BodySize = size; _vm.Prefs.LineHeight = size * Math.Clamp(_vm.Prefs.LineHeight / old, 1.2, 2.4); _vm.SaveSettings();
    }

    private static string PlainReaderTitle(string value) => EmojiToken.Replace(value, token =>
        Emoji.ContainsKey(token.Value) ? "（" + token.Value[(token.Value.IndexOf('_') + 1)..^1] + "）" : token.Value);

    private TextBlock ReaderTitle(string value, double size, bool bold = false)
    {
        var label = PlainReaderTitle(value);
        if (_vm.Prefs.NoImages || label == value) return Text(label, size, bold);
        var text = Text("", size, bold);
        PopulateInlines(text, [new(value)], size);
        Avalonia.Automation.AutomationProperties.SetName(text, label);
        return text;
    }

    private Control BuildArticle(Article article)
    {
        var story = article.Story;
        var stack = Stack(0);
        var title = ReaderTitle(story.Title, 24, true);
        title.LineHeight = 34;
        title.Margin = new(0, 0, 0, 12);
        stack.Children.Add(title);
        var authorButton = Button(story.Author, () => { if (story.Profile?.Id is { Length: > 0 } id) Run(() => _vm.NavigateAsync("destination", new("author", id, story.Author))); }, "flat"); authorButton.Classes.Add("commentName");
        var metadata = story.Date + (story.ReadMinutes is { } minutes ? " · " + minutes + " " + T("分钟") : "");
        var row = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new(0, 0, 0, 18) };
        row.Children.Add(Row(8, Avatar(_vm.Prefs.NoImages ? null : story.Profile?.Avatar, 26), authorButton, Text(metadata, 12, muted: true)));
        if (HeyboxActions)
        {
            var follow = AsyncButton(T(article.Followed == true ? "已关注" : "关注"), () => Social("follow")); follow.Classes.Add("accentOutline"); follow.Classes.Add("filterChip"); follow.MinWidth = 54; Grid.SetColumn(follow, 1); row.Children.Add(follow);
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
            var heroHeight = Math.Clamp(Math.Min(744, ReaderWidth - 56) * .54, 170, 426);
            var picture = Picture(hero, heroHeight);
            if (picture is RemoteImage remote)
            {
                remote.PreserveAspectRatio = true;
                remote.MaxHeight = 560;
            }
            picture.Margin = new(0, 0, 0, 14);
            stack.Children.Add(picture);
            picture.Tapped += (_, _) => { if (!hero.StartsWith("design:")) Run(() => ImageViewer(gallery, 0)); };
        }
        if (story.Source == Source.STEAM) stack.Children.Add(BuildTranslationBar());
        if (story.Source == Source.STEAM && _vm.TranslationMode == "translated" && _vm.TranslatedBlocks != null) blocks = _vm.TranslatedBlocks;
        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];
            if (block.Kind == BlockKind.Image && block.Url == hero) continue;
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
