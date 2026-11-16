using System.Text.Json.Serialization;

namespace HeyRead.Core;

public enum Source { HEYBOX, STEAM, DEMO }
public enum BlockKind { Paragraph, Heading, Image, Quote, ListItem, Game, Video, ExternalVideo, Notice }
public sealed record RichRun(string Text, bool Bold = false, bool Italic = false, string? Url = null, bool Code = false);
public sealed record ArticleBlock(BlockKind Kind, string Text = "", string? Url = null, string? Caption = null, int Level = 0, int? AppId = null, IReadOnlyList<ArticleBlock>? Children = null, IReadOnlyList<RichRun>? Runs = null);
public sealed record Label(string Id, string Name);
public sealed record Author(string Id = "", string Name = "", string? Avatar = null, string? Location = null) { public override string ToString() => Name; }
public sealed record Story(string Id, Source Source, string Title, string Excerpt, string Body = "", string Author = "", string Date = "", string? Image = null, string? Url = null, int? AppId = null, long? PublishedAt = null, string? HSrc = null, Author? Profile = null, IReadOnlyList<Label>? Sections = null, IReadOnlyList<Label>? Topics = null, IReadOnlyList<ArticleBlock>? Blocks = null, string? Notice = null)
{
    public override string ToString() => Title;
    [JsonIgnore] public string Key => $"{Source}:{Id}";
    [JsonIgnore] public string Metadata => $"{Author}  ·  {Date}";
}
public sealed record Page<T>(IReadOnlyList<T> Items, bool HasMore, int NextOffset = 0, string? Cursor = null);
public sealed record Game(int AppId, string Name, string? Image);
public sealed record Folder(string Id, string Name) { public override string ToString() => Name; }
public sealed record Comment(string Id, string RootId, string Text, Author Author, string Date, int Supports, bool Supported, IReadOnlyList<Comment> Replies, IReadOnlyList<string> Images);
public sealed record Article(Story Story, IReadOnlyList<Comment> Comments, bool HasMoreComments = false, string? AuthCode = null, bool? Liked = null, bool? Favorited = null, bool? Followed = null);
public sealed record Session(string Cookie, string UserId, string Nickname, string Avatar)
{
    [JsonIgnore] public bool IsValid => !string.IsNullOrWhiteSpace(UserId) && !Cookie.Any(char.IsControl) && Cookie.Split(';').Any(x => x.Split('=', 2) is [var name, var value] && new[] { "pkey", "user_pkey", "x_pkey" }.Contains(name.Trim()) && !string.IsNullOrWhiteSpace(value));
    public override string ToString() => $"Session({UserId}, <redacted>)";
}
public sealed record QrChallenge(string Url, string Key, DateTimeOffset Expires);
public sealed record QrPoll(string State, Session? Session = null);
public sealed record Destination(string Kind, string Id, string Name);
public sealed class ApiException(string message, string? status = null) : Exception(message) { public string? Status { get; } = status; }
public interface ISessionStore { Session? Load(); void Save(Session? session); }
public interface ITranslationService { Task<string[]> TranslateAsync(string[] text, string target, CancellationToken ct); }

public sealed record AuthorPage(Author Author, string Bio, long? Following, long? Followers, long? Likes);
