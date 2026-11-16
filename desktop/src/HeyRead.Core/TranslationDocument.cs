namespace HeyRead.Core;

/// <summary>Maps an ordered translation batch back into the original media/quote/list hierarchy.</summary>
public sealed class TranslationDocument
{
    public IReadOnlyList<ArticleBlock> Original { get; }
    public string[] Input { get; }
    public TranslationDocument(IReadOnlyList<ArticleBlock> blocks)
    {
        Original = blocks;
        var input = new List<string>(); Visit(blocks, input); Input = input.ToArray();
    }
    private static void Visit(IReadOnlyList<ArticleBlock> blocks, List<string> output)
    {
        foreach (var block in blocks)
        {
            if (Translatable(block) && !string.IsNullOrWhiteSpace(block.Text)) output.Add(block.Text);
            if (block.Kind == BlockKind.Image && !string.IsNullOrWhiteSpace(block.Caption)) output.Add(block.Caption);
            if (block.Children != null) Visit(block.Children, output);
        }
    }
    public IReadOnlyList<ArticleBlock> Apply(IReadOnlyList<string> output)
    {
        if (output.Count != Input.Length || output.Any(string.IsNullOrWhiteSpace)) throw new InvalidOperationException("翻译结果不完整，请重试");
        var index = 0;
        IReadOnlyList<ArticleBlock> Map(IReadOnlyList<ArticleBlock> blocks) => blocks.Select(block =>
        {
            var result = block;
            if (Translatable(block) && !string.IsNullOrWhiteSpace(block.Text))
            {
                var text = output[index++];
                var links = block.Runs?.Where(r => r.Url != null || r.ImageUrl != null).ToArray() ?? [];
                result = block with { Text = text, Runs = links.Length == 0 ? null : new[] { new RichRun(text) }.Concat(links.Select(r => r with { Text = r.ImageUrl == null ? " [" + r.Text + "]" : r.Text })).ToArray() };
            }
            if (block.Kind == BlockKind.Image && !string.IsNullOrWhiteSpace(block.Caption)) result = result with { Caption = output[index++] };
            if (block.Children != null) result = result with { Children = Map(block.Children) };
            return result;
        }).ToArray();
        return Map(Original);
    }
    private static bool Translatable(ArticleBlock block) => block.Kind is BlockKind.Paragraph or BlockKind.Heading or BlockKind.ListItem;
}
