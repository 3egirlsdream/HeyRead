using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using HeyRead.Core;
using HeyRead.Desktop.Controls;
using static HeyRead.Desktop.Controls.Ui;

namespace HeyRead.Desktop.Views;

public sealed partial class MainWindow
{
    private TextBlock? _discussionCount;
    private ContentControl? _discussionStatus;
    private readonly Dictionary<string, ReplyThread> _replyThreads = [];
    private string? _replyStoryKey;

    private sealed class ReplyThread(Comment root, Comment[] preview)
    {
        public Comment Root { get; set; } = root;
        public Comment[] Preview { get; set; } = preview;
        public List<Comment> Loaded { get; } = [];
        public HashSet<string> Cursors { get; } = [];
        public ContentControl? View { get; set; }
        public bool Expanded { get; set; } = preview.Length > 0;
        public bool Loading { get; set; }
        public bool Started { get; set; }
        public bool HasMore { get; set; }
        public string? Cursor { get; set; }
        public string? Error { get; set; }
        public IEnumerable<Comment> Items => Preview.Concat(Loaded).DistinctBy(reply => reply.Id);
        public int Total => Math.Max(Root.ChildCount ?? 0, Preview.Length);
        public bool CanLoad => Started ? HasMore : Total > Preview.Length;
    }

    private void RenderComments(Article article)
    {
        if (_replyStoryKey != article.Story.Key)
        {
            _replyThreads.Clear();
            _replyStoryKey = article.Story.Key;
        }
        var identity = (article.Story.Key, _vm.Prefs.NoImages, _vm.Prefs.BodyFont, _vm.Prefs.Language);
        if (!Equals(_commentContentIdentity, identity))
        {
            _commentContentIdentity = identity;
            var heading = Text(T("评论"), 18, true);
            _discussionCount = Text("", 12, muted: true);
            var compose = ActionButton("edit", T("写评论"), () => CommentDialog(null));
            var header = Columns("*,Auto", Row(7, heading, _discussionCount), compose);
            header.Margin = new(0, 10, 0, 6);
            var rows = new ItemsControl
            {
                ItemsSource = _vm.Comments,
                ItemTemplate = new FuncDataTemplate<Comment>((comment, _) => CommentView(comment))
            };
            Grid.SetIsSharedSizeScope(rows, true);
            _discussionStatus = new ContentControl();
            var section = Stack(0, Line(), header, rows, _discussionStatus);
            section.Margin = new(0, 16, 0, 0);
            _commentsSection.Content = section;
        }
        _discussionCount!.Text = Counter(article).Trim();
        if (_vm.CommentsError.Length > 0)
            _discussionStatus!.Content = Stack(6, Text(T(_vm.CommentsError), 12, muted: true),
                AsyncButton(T("重试"), _vm.MoreCommentsAsync, "compactAction"));
        else if (_vm.CommentsLoading)
            _discussionStatus!.Content = new ProgressBar { IsIndeterminate = true, Height = 2, Margin = new(0, 8) };
        else if (_vm.Comments.Count == 0)
            _discussionStatus!.Content = Text(T(_vm.Api.Session == null ? "登录后查看完整评论" : "暂无内容"), 13, muted: true);
        else
            _discussionStatus!.Content = null;
    }

    private IReadOnlyList<Comment> CommentGallery() => _vm.Comments.SelectMany(root =>
        _replyThreads.TryGetValue(root.Id, out var thread) && thread.Expanded
            ? thread.Items.Prepend(root)
            : new[] { root }).ToArray();

    private static IEnumerable<Comment> FlattenThread(Comment root)
    {
        yield return root;
        foreach (var child in root.Replies.SelectMany(FlattenThread)) yield return child;
    }

    private Task OpenCommentImage(Comment selected, int index)
    {
        var thread = CommentGallery();
        var offset = 0;
        foreach (var comment in thread)
        {
            if (comment.Id == selected.Id)
                return ImageViewer(thread.SelectMany(item => item.Images).ToArray(), offset + index);
            offset += comment.Images.Count;
        }
        return Task.CompletedTask;
    }

    private Control CommentView(Comment comment)
    {
        var author = comment.Author;
        var name = CommentAuthor(author);
        var meta = Text(CommentMetadata(comment), 10.5, muted: true);
        var identity = Stack(0, name, meta);
        var avatar = Avatar(_vm.Prefs.NoImages ? null : author.Avatar, 27);
        avatar.VerticalAlignment = VerticalAlignment.Top;
        avatar.Margin = new(0, 1, 0, 0);

        var actions = CommentActions(comment, inline: false);
        actions.VerticalAlignment = VerticalAlignment.Top;
        var byline = Columns("*,Auto", identity, actions);
        var text = CommentText(comment.Text, 13);
        var body = Stack(4, byline, text);
        if (CommentImages(comment) is { } images) body.Children.Add(images);
        var preview = comment.Replies.SelectMany(FlattenThread).DistinctBy(reply => reply.Id).ToArray();
        if (preview.Length > 0 || comment.ChildCount > 0)
        {
            if (!_replyThreads.TryGetValue(comment.Id, out var thread))
                _replyThreads[comment.Id] = thread = new ReplyThread(comment, preview);
            else
            {
                thread.Root = comment;
                thread.Preview = preview;
            }
            thread.View = new ContentControl { Margin = new(0, 1, 0, 0) };
            body.Children.Add(thread.View);
            UpdateReplyThread(thread);
        }

        var row = Columns("Auto,*", avatar, body);
        row.ColumnSpacing = 8;
        var border = Brush(new Border { Child = row, Padding = new(0, 9, 0, 9), BorderThickness = new(0, 0, 0, 1) },
            Border.BorderBrushProperty, "Line");
        border.Classes.Add("commentRoot");
        return border;
    }

    private Control InlineReplyView(Comment comment)
    {
        var text = CommentText(comment.Text, 12);
        var inlines = text.Inlines!;
        var author = comment.Author;
        var name = author.Name.Length > 0 ? author.Name : "—";
        var prefix = string.IsNullOrWhiteSpace(comment.ReplyTo) ? "  " : $"  {T("回复")} {comment.ReplyTo}  ";
        inlines.Insert(0, Brush(new Run(prefix) { FontSize = 11 }, TextElement.ForegroundProperty, "Muted"));
        inlines.Insert(0, Brush(new Run(name), TextElement.ForegroundProperty, "Accent"));
        if (author.Id.Length > 0) LinkReplyAuthor(text, author, name.Length);
        if (CommentMetadata(comment) is { Length: > 0 } meta)
            inlines.Add(Brush(new Run("  " + meta) { FontSize = 10.5 }, TextElement.ForegroundProperty, "Muted"));

        var body = Stack(2, text);
        if (CommentImages(comment) is { } images) body.Children.Add(images);
        var actions = CommentActions(comment, inline: true);
        actions.VerticalAlignment = VerticalAlignment.Top;
        var row = Columns("*,Auto", body, actions);
        row.ColumnSpacing = 5;
        row.ColumnDefinitions[1].SharedSizeGroup = "InlineReplyActions";
        row.Classes.Add("commentReply");
        return row;
    }

    private SelectableTextBlock CommentText(string value, double size)
    {
        var text = RichText(new(BlockKind.Paragraph, value), size, double.NaN);
        text.LineSpacing = 2;
        text.Background = Brushes.Transparent;
        return text;
    }

    private void LinkReplyAuthor(SelectableTextBlock text, Author author, int nameLength)
    {
        var pressedAuthor = false;
        bool IsAuthorHit(PointerEventArgs e)
        {
            var hit = text.TextLayout.HitTestPoint(e.GetPosition(text) - new Point(text.Padding.Left, text.Padding.Top));
            return hit.IsInside && hit.CharacterHit.FirstCharacterIndex >= 0 && hit.CharacterHit.FirstCharacterIndex < nameLength;
        }

        text.AddHandler(PointerPressedEvent, (_, e) =>
        {
            pressedAuthor = e.ClickCount == 1 && e.KeyModifiers == KeyModifiers.None &&
                e.GetCurrentPoint(text).Properties.IsLeftButtonPressed && IsAuthorHit(e);
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        text.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (pressedAuthor && e.InitialPressMouseButton == MouseButton.Left && text.SelectionStart == text.SelectionEnd && IsAuthorHit(e))
                Run(() => NavigateToCommentAuthor(author));
            pressedAuthor = false;
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        text.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || text.SelectionStart != text.SelectionEnd) return;
            e.Handled = true;
            Run(() => NavigateToCommentAuthor(author));
        };
    }

    private void UpdateReplyThread(ReplyThread thread)
    {
        if (thread.View == null) return;
        if (!thread.Expanded)
        {
            var expand = Button(F("展开 {0} 条回复", Math.Max(thread.Total, thread.Items.Count())), () => Run(async () =>
            {
                thread.Expanded = true;
                UpdateReplyThread(thread);
                if (!thread.Started && thread.CanLoad && thread.Preview.Length == 0) await LoadInlineReplies(thread);
                QueuePagination();
            }), "flat");
            expand.Classes.Add("threadToggle");
            expand.HorizontalAlignment = HorizontalAlignment.Left;
            thread.View.Content = expand;
            return;
        }

        var list = Stack(3);
        foreach (var reply in thread.Items) list.Children.Add(InlineReplyView(reply));
        if (thread.Loading)
            list.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 2, Margin = new(0, 5) });
        else if (thread.Error != null)
            list.Children.Add(Row(5, Text(T(thread.Error), 11, muted: true), CommentAction("arrow_clockwise", T("重试"), () => LoadInlineReplies(thread))));
        else if (!thread.Started && thread.CanLoad)
            list.Children.Add(CommentAction("chevron_down", T("查看更多回复"), () => LoadInlineReplies(thread)));

        var collapse = Button(T("收起回复"), () =>
        {
            thread.Expanded = false;
            UpdateReplyThread(thread);
        }, "flat");
        collapse.Classes.Add("threadToggle");
        collapse.HorizontalAlignment = HorizontalAlignment.Stretch;
        var branch = Brush(new Border { Child = Stack(4, list, Line(), collapse), Padding = new(8, 5, 8, 0), CornerRadius = new(6) },
            Border.BackgroundProperty, "Subtle");
        branch.Classes.Add("commentBranch");
        thread.View.Content = branch;
    }

    private async Task LoadInlineReplies(ReplyThread thread)
    {
        var storyKey = _replyStoryKey;
        if (_vm.VisualFixture || thread.Loading || !thread.Expanded || !thread.CanLoad || storyKey != _vm.Article?.Story.Key) return;
        thread.Loading = true;
        thread.Error = null;
        UpdateReplyThread(thread);
        try
        {
            var page = await _vm.RepliesAsync(thread.Root, thread.Cursor, _updateLifetime.Token);
            if (storyKey != _vm.Article?.Story.Key) return;
            var known = thread.Items.Select(reply => reply.Id).Append(thread.Root.Id).ToHashSet();
            thread.Loaded.AddRange(page.Items.SelectMany(FlattenThread).Where(reply => known.Add(reply.Id)));
            thread.Started = true;
            thread.HasMore = page.HasMore && !string.IsNullOrWhiteSpace(page.Cursor) && thread.Cursors.Add(page.Cursor);
            thread.Cursor = page.Cursor;
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { thread.Error = e.Message; }
        finally
        {
            thread.Loading = false;
            if (storyKey == _vm.Article?.Story.Key)
            {
                UpdateReplyThread(thread);
                QueuePagination();
            }
        }
    }

    private void LoadRepliesNearViewport()
    {
        if (_commentsSection.Content == null || !_readerScroll.IsEffectivelyVisible || _readerScroll.Viewport.Height <= 0 ||
            _replyThreads.Values.Any(thread => thread.Loading)) return;
        foreach (var thread in _replyThreads.Values)
        {
            if (!thread.Expanded || !thread.Started || !thread.HasMore || thread.Error != null ||
                thread.View is not { IsEffectivelyVisible: true } view ||
                view.TranslatePoint(new Point(0, view.Bounds.Height), _readerScroll) is not { } end ||
                end.Y < 0 || end.Y > _readerScroll.Viewport.Height + 112) continue;
            Run(() => LoadInlineReplies(thread));
            break;
        }
    }

    private static string CommentMetadata(Comment comment) =>
        string.Join(" · ", new[] { comment.Date, comment.Author.Location }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private Control CommentAuthor(Author author)
    {
        var label = Text(author.Name.Length > 0 ? author.Name : "—", 13, true);
        if (author.Id.Length == 0) return label;
        var profile = Button("", () => Run(() => NavigateToCommentAuthor(author)), "flat");
        profile.Content = label;
        profile.Padding = default;
        profile.MinHeight = 0;
        profile.HorizontalAlignment = HorizontalAlignment.Left;
        Avalonia.Automation.AutomationProperties.SetName(profile, author.Name);
        return profile;
    }

    private Task NavigateToCommentAuthor(Author author)
    {
        SaveOffsets();
        return _vm.NavigateAsync("destination", new("author", author.Id, author.Name));
    }

    private Control? CommentImages(Comment comment)
    {
        if (comment.Images.Count == 0) return null;
        var images = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 5 };
        for (var i = 0; i < comment.Images.Count; i++)
        {
            var index = i;
            var label = T("图片 · 点击查看");
            var image = AsyncButton(_vm.Prefs.NoImages ? label : "", () => OpenCommentImage(comment, index), "flat");
            image.Classes.Add("commentAction");
            if (!_vm.Prefs.NoImages)
            {
                image.Padding = default;
                image.Content = Picture(comment.Images[index], 76, 108);
                Avalonia.Automation.AutomationProperties.SetName(image, label);
            }
            images.Children.Add(image);
        }
        return images;
    }

    private StackPanel CommentActions(Comment comment, bool inline)
    {
        var storyKey = _vm.Article?.Story.Key;
        var current = comment;
        var support = new Button { Classes = { "flat", "commentAction" } };
        void SetState()
        {
            var color = current.Supported ? "Accent" : "Muted";
            var glyph = Icons.Get("thumb_like", 13, color);
            support.Content = inline && current.Supports == 0 ? glyph : Row(3, glyph,
                Brush(Text(current.Supports.ToString(), 11), TextBlock.ForegroundProperty, color));
            var label = T(current.Supported ? "已喜欢" : "喜欢");
            ToolTip.SetTip(support, label);
            Avalonia.Automation.AutomationProperties.SetName(support, label + " " + current.Supports);
        }
        SetState();
        support.Click += (_, _) => Run(async () =>
        {
            if (_vm.VisualFixture || storyKey == null) return;
            support.IsEnabled = false;
            try
            {
                if (_vm.Api.Session == null) await LoginDialog();
                if (_vm.Api.Session == null || _vm.Article?.Story.Key != storyKey) return;
                await _vm.SocialAsync("support", current);
                current = current with { Supported = !current.Supported, Supports = Math.Max(0, current.Supports + (current.Supported ? -1 : 1)) };
                if (inline && _replyThreads.TryGetValue(current.RootId, out var thread))
                {
                    var index = thread.Loaded.FindIndex(reply => reply.Id == current.Id);
                    if (index >= 0) thread.Loaded[index] = current;
                }
                SetState();
            }
            finally { support.IsEnabled = true; }
        });
        var replyAction = CommentAction("comment", T("回复"), () => CommentDialog(comment), inline);
        if (inline)
        {
            support.Classes.Add("inlineCommentAction");
            replyAction.Classes.Add("inlineCommentAction");
        }
        return Row(3, replyAction, support);
    }

    private Button CommentAction(string icon, string label, Func<Task> action, bool iconOnly = false)
    {
        var button = AsyncButton("", action, "flat");
        button.Classes.Add("commentAction");
        var glyph = Icons.Get(icon, 13, "Muted");
        button.Content = iconOnly ? glyph : Row(4, glyph, Text(label, 11, muted: true));
        ToolTip.SetTip(button, label);
        Avalonia.Automation.AutomationProperties.SetName(button, label);
        return button;
    }
}
