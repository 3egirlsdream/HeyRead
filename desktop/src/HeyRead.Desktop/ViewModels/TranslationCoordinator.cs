using System.Security.Cryptography;
using System.Text;
using HeyRead.Core;
namespace HeyRead.Desktop.ViewModels;
public sealed partial class MainViewModel
{
    public IReadOnlyList<ArticleBlock>? TranslatedBlocks { get; private set; }
    public string TranslationState { get; private set; } = "idle";
    public string TranslationError { get; private set; } = "";
    private string _translationMode = "original";
    public string TranslationMode { get => _translationMode; set { if (SetProperty(ref _translationMode, value)) ContentChanged?.Invoke(); } }
    private readonly Dictionary<string, (string[] Text, DateTimeOffset Saved)> _translations = [];
    private void ClearTranslation() { Translated = ""; TranslatedBlocks = null; TranslationState = "idle"; TranslationError = ""; _translationMode = "original"; }
    public void CancelTranslation() { Cancel(ref _translate); Translating = false; TranslationState = "idle"; ContentChanged?.Invoke(); }
    public async Task TranslateAsync()
    {
        var story = Article?.Story; if (story?.Source != Source.STEAM || Translating || IsArticleBlocked) return;
        Cancel(ref _translate); var token = _translate.Token;
        var document = new TranslationDocument(story.Blocks ?? ContentParser.Html(story.Body));
        if (document.Input.Length == 0) { TranslationError = "此公告没有可翻译的文字"; TranslationState = "failed"; ContentChanged?.Invoke(); return; }
        var language = Prefs.Language == "zh-TW" ? "zh-Hant" : "zh-Hans";
        var key = story.Key + language + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\0', document.Input))));
        Translating = true; TranslationState = "preparing"; TranslationError = ""; ContentChanged?.Invoke();
        try
        {
            var text = _translations.TryGetValue(key, out var cached) ? cached.Text : await _translation.TranslateAsync(document.Input, language, token);
            token.ThrowIfCancellationRequested(); if (Article?.Story.Key != story.Key) return;
            TranslatedBlocks = document.Apply(text); Translated = string.Join("\n\n", text); TranslationState = "ready"; _translationMode = "translated";
            _translations[key] = (text, DateTimeOffset.UtcNow);
            while (_translations.Count > 8 || _translations.Values.Sum(x => x.Text.Sum(t => t.Length)) > 2000000) _translations.Remove(_translations.MinBy(x => x.Value.Saved).Key);
        }
        catch (OperationCanceledException) { if (!token.IsCancellationRequested) TranslationState = "idle"; }
        catch (Exception e) { if (!token.IsCancellationRequested) { TranslationState = "failed"; TranslationError = e.Message; Toast = "翻译失败：" + e.Message; } }
        finally { if (!token.IsCancellationRequested) { Translating = false; ContentChanged?.Invoke(); } }
    }
}
