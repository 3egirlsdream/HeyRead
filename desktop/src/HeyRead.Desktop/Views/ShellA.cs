using Avalonia.Input.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using HeyRead.Core;
using HeyRead.Desktop.Controls;
using static HeyRead.Desktop.Controls.Ui;

namespace HeyRead.Desktop.Views;
public sealed partial class MainWindow
{
    private Control Picture(string? url, double height, double? width = null)
    {
        if (_vm.VisualFixture && url?.StartsWith("design:") == true) return _referenceAssets.Artwork(url[7..], height, width);
        var image = new RemoteImage(_images, url, height); if (width != null) image.Width = width.Value; return image;
    }
    private Control Avatar(string? url, double size)
    {
        var artwork = _vm.VisualFixture && url == "design:account" && size > 32 ? "design:account-large" : url;
        var content = artwork == null ? (Control)Icons.Get("person", size * .6, "Muted") : Picture(artwork, size, size);
        return Brush(new Border { Child = content, Width = size, Height = size, CornerRadius = new(size / 2), ClipToBounds = true }, Border.BackgroundProperty, "Subtle");
    }
    private Control StoryRow(Story? story)
    {
        if (story == null) return new Border();
        var title = Text(story.Title, 13); title.FontWeight = FontWeight.Medium; title.MaxLines = 2; title.LineHeight = 18; title.TextTrimming = TextTrimming.CharacterEllipsis; ToolTip.SetTip(title, story.Title);
        var more = DirectoryOverview ? new Button { Content = Icons.Get("more_horizontal", 16), Width = 30, Classes = { "compactAction", "iconOnly" } } : null;
        var content = more != null ? FeedContent(story, title, more) : CompactContent(story, title);
        var card = new Border { Child = content, Padding = new(10, 7), MinHeight = 62, CornerRadius = new(8) };
        if (DirectoryOverview) card.Padding = new(8, 6, 8, 8);
        card.Classes.Add("storyCard");
        var outer = Stack(0, card); outer.Margin = new(10, 3); outer.MaxWidth = DirectoryOverview ? 1050 : 760;
        var menu = new ContextMenu();
        foreach (var (label, action) in new (string, Action)[] { ("打开原文", () => OpenUrl(story.Url)), ("复制链接", () => Run(() => Clipboard?.SetTextAsync(story.Url ?? "") ?? Task.CompletedTask)), ("屏蔽关键词", () => Run(() => RuleDialog(new(RuleKind.KEYWORD, "")))), ("屏蔽作者", () => { if (story.Profile?.Id is { Length: > 0 } id) AddRule(new(RuleKind.AUTHOR, id, story.Author, Source: Source.HEYBOX)); }) }) { var item = new MenuItem { Header = T(label) }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        outer.ContextMenu = menu;
        if (more != null)
        {
            more.Classes.Add("flat");
            Avalonia.Automation.AutomationProperties.SetName(more, T("更多操作"));
            ToolTip.SetTip(more, T("更多操作"));
            more.Click += (_, _) => menu.Open(more);
        }
        return outer;
    }
    private Control FeedContent(Story story, TextBlock title, Button more)
    {
        title.FontSize = 15;
        title.LineHeight = 20;
        var summary = Text(story.Excerpt, 13);
        summary.LineHeight = 18;
        summary.MaxLines = 3;
        summary.TextTrimming = TextTrimming.CharacterEllipsis;
        var feedDetail = Stack(6, title, summary);
        var read = AsyncButton("", () => OpenStoryAsync(story), "flat");
        var readLabel = Brush(Text(T("正文") + " · " + story.Title, 14, true), TextBlock.ForegroundProperty, "Accent");
        readLabel.MaxLines = 1;
        readLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        read.Content = Brush(new Border { Child = readLabel, Padding = new(9, 0, 0, 0), BorderThickness = new(3, 0, 0, 0) }, Border.BorderBrushProperty, "Accent");
        read.HorizontalContentAlignment = HorizontalAlignment.Left;
        read.MinHeight = 23;
        read.Padding = new(0);
        feedDetail.Children.Add(read);
        var snapshot = _vm.Article?.Story.Key == story.Key ? _vm.Article : null;
        if (story.Source == Source.HEYBOX || _vm.VisualFixture)
        {
            var comments = story.CommentCount is { } count ? " " + count : "";
            var likes = snapshot?.LikeCount is { } likeCount ? " " + likeCount : "";
            feedDetail.Children.Add(new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 10, LineSpacing = 4, Children = {
                FeedAction("heart", T(snapshot?.Liked == true ? "已喜欢" : "喜欢") + likes, story, "like", snapshot?.Liked),
                FeedAction("comment", T("评论") + comments, story, "comments"),
                FeedAction("bookmark", T(snapshot?.Favorited == true ? "已收藏" : "收藏"), story, "favorite", snapshot?.Favorited) } });
        }
        Control feedBody = feedDetail;
        if (!_vm.Prefs.NoImages && story.Image != null)
        {
            var imageWidth = _vm.Prefs.CardLayout == "Gallery" ? 126 : 94;
            var artwork = _vm.VisualFixture && story.Image is "design:1" or "design:2" or "design:5" ? story.Image + "-feed" : story.Image;
            var image = Picture(artwork, 62, imageWidth);
            image.VerticalAlignment = VerticalAlignment.Top;
            feedBody = Columns($"*,10,{imageWidth}", feedDetail, new Border(), image);
        }
        var game = story.Blocks?.FirstOrDefault(block => block.Kind == BlockKind.Game && block.AppId != null);
        if (game != null)
        {
            var link = AsyncButton("", () => _vm.NavigateAsync("news", game: new(game.AppId!.Value, game.Text, null)));
            link.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            link.Padding = new(12);
            link.MinHeight = 110;
            Brush(link, Avalonia.Controls.Button.BackgroundProperty, "Subtle");
            var name = string.IsNullOrWhiteSpace(game.Text) ? $"AppID · {game.AppId}" : game.Text;
            var label = Stack(8, Text(name, 16, true), Text("Steam", 14, muted: true));
            label.VerticalAlignment = VerticalAlignment.Center;
            var artwork = _vm.VisualFixture && game.Url == "design:game" ? "design:feed-game" : $"https://cdn.akamai.steamstatic.com/steam/apps/{game.AppId}/header.jpg";
            link.Content = _vm.Prefs.NoImages ? label : Columns("90,14,*,16", Picture(artwork, 86, 90), new Border(), label, Icons.Get("chevron_right", 15, "Muted"));
            link.VerticalAlignment = VerticalAlignment.Center;
            feedBody = Stack(7, feedBody, link);
        }
        var byline = Row(12, Avatar(_vm.Prefs.NoImages ? null : story.Profile?.Avatar, 36), Text(story.Author, 17, true), Text(story.Date, 14, muted: true));
        return Stack(8, Columns("*,32", byline, more), feedBody);
    }

    private Button FeedAction(string icon, string label, Story story, string action, bool? displayedState = null)
    {
        var button = AsyncButton("", async () =>
        {
            await OpenStoryAsync(story);
            if (_vm.Article?.Story.Key != story.Key || !IsDirectoryPage) return;
            Render();
            if (_vm.IsArticleBlocked || _vm.DetailError.Length != 0) return;
            if (action == "comments") JumpToComments();
            else
            {
                var currentState = action == "like" ? _vm.Article.Liked : _vm.Article.Favorited;
                if ((currentState == true) == (displayedState == true)) await Social(action, expectedStory: story.Key);
            }
        });
        button.Classes.Add("compactAction");
        button.Content = Row(5, Icons.Get(icon, 15), Text(label, 12));
        return button;
    }

    private Control CompactContent(Story story, TextBlock title)
    {
        var author = Text(story.Author + (story.Date.Length == 0 ? "" : " · " + story.Date), 11.5, muted: true); author.MaxLines = 1; author.TextTrimming = TextTrimming.CharacterEllipsis;
        var metadata = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6 }; metadata.Children.Add(author);
        if (story.CommentCount is { } count)
        {
            var comments = Row(3, Icons.Get("comment", 12, "Muted"), Text(count.ToString(), 11.5, muted: true));
            Grid.SetColumn(comments, 1);
            metadata.Children.Add(comments);
        }
        var detail = Stack(3, title, metadata); detail.VerticalAlignment = VerticalAlignment.Center;
        Control content = detail;
        if (!_vm.Prefs.NoImages && story.Image != null)
        {
            content = _vm.Prefs.CardLayout == "Gallery" ? Stack(6, Picture(story.Image, 132), detail) : Columns("*,10,90", detail, new Border(), Picture(story.Image, 48, 90));
        }
        if (_vm.Prefs.CardLayout == "Comfortable") content = Stack(6, content, Text(story.Excerpt, 12, muted: true));
        return content;
    }
}
