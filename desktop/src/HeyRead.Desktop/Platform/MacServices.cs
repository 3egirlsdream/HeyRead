using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using HeyRead.Core;
namespace HeyRead.Desktop.Platform;

public static class Native
{
    [DllImport("libHeyReadNative.dylib")] internal static extern nint hr_gif_create([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport("libHeyReadNative.dylib")] internal static extern void hr_gif_release(nint view);
    [DllImport("libHeyReadNative.dylib")] internal static extern int hr_reduce_motion();
    private const string Lib = "libHeyReadNative.dylib";
    [DllImport(Lib)] private static extern void hr_free(nint p);
    [DllImport(Lib)] private static extern nint hr_font_register([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport(Lib)] private static extern nint hr_accent();
    [DllImport(Lib)] internal static extern nint hr_video_create([MarshalAs(UnmanagedType.LPUTF8Str)] string url);
    [DllImport(Lib)] internal static extern void hr_video_release(nint view);
    [DllImport(Lib)] private static extern void hr_share([MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Lib)] private static extern void hr_verify([MarshalAs(UnmanagedType.LPUTF8Str)] string url);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void TranslationCallback(long id, nint result);
    [DllImport(Lib)] private static extern void hr_translate(long id, [MarshalAs(UnmanagedType.LPUTF8Str)] string input, TranslationCallback callback);
    [DllImport(Lib)] private static extern void hr_translate_cancel(long id);
    [DllImport(Lib)] private static extern void hr_captcha(long id, [MarshalAs(UnmanagedType.LPUTF8Str)] string appId, TranslationCallback callback);
    [DllImport(Lib)] private static extern void hr_captcha_cancel(long id);
    private static readonly ConcurrentDictionary<long, TaskCompletionSource<CaptchaProof>> Captchas = new();
    private static readonly TranslationCallback CaptchaCallback = (id, result) => { if (!Captchas.TryRemove(id, out var task)) return; try { task.TrySetResult(CaptchaProof.Parse(Marshal.PtrToStringUTF8(result) ?? "{}")); } catch (Exception e) { task.TrySetException(e); } };
    public static async Task<CaptchaProof> VerifyCaptchaAsync(CaptchaChallenge challenge, CancellationToken ct)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException(); ct.ThrowIfCancellationRequested(); var id = Interlocked.Increment(ref _id); var task = new TaskCompletionSource<CaptchaProof>(TaskCreationOptions.RunContinuationsAsynchronously); Captchas[id] = task;
        await Dispatcher.UIThread.InvokeAsync(() => hr_captcha(id, challenge.AppId, CaptchaCallback));
        using var reg = ct.Register(() => Dispatcher.UIThread.Post(() => { if (Captchas.TryRemove(id, out var pending)) pending.TrySetCanceled(ct); hr_captcha_cancel(id); })); return await task.Task;
    }
    private static readonly TranslationCallback Callback = Complete;
    private static readonly ConcurrentDictionary<long, TaskCompletionSource<string[]>> Pending = new();
    private static long _id;
    public static string? TakeString(nint p) { if (p == 0) return null; try { return Marshal.PtrToStringUTF8(p); } finally { hr_free(p); } }
    public static string? RegisterFont(string path) => OperatingSystem.IsMacOS() ? TakeString(hr_font_register(path)) : null;
    [DllImport(Lib)] private static extern int hr_font_unregister([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    public static void UnregisterFont(string path) { if (OperatingSystem.IsMacOS()) hr_font_unregister(path); }
    [DllImport(Lib)] private static extern nint hr_font_inspect([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    public static string? InspectFont(string path) => OperatingSystem.IsMacOS() ? TakeString(hr_font_inspect(path)) : null;
    public static string Accent() => OperatingSystem.IsMacOS() ? TakeString(hr_accent()) ?? "#2563D9" : "#2563D9";
    [DllImport(Lib)] private static extern void hr_language([MarshalAs(UnmanagedType.LPUTF8Str)] string language);
    public static void SetLanguage(string language) { if (OperatingSystem.IsMacOS()) hr_language(language); }
    public static void Share(string value) { if (OperatingSystem.IsMacOS()) hr_share(value); }
    public static void Verify(string url) { if (OperatingSystem.IsMacOS()) hr_verify(url); }
    public static async Task<string[]> TranslateAsync(string[] text, string target, CancellationToken ct)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Apple 翻译需要 macOS 15+");
        ct.ThrowIfCancellationRequested();
        var id = Interlocked.Increment(ref _id); var tcs = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously); Pending[id] = tcs;
        await Dispatcher.UIThread.InvokeAsync(() => hr_translate(id, JsonSerializer.Serialize(new { text, target }), Callback));
        using var reg = ct.Register(() => Dispatcher.UIThread.Post(() => { if (Pending.TryRemove(id, out var task)) task.TrySetCanceled(ct); hr_translate_cancel(id); }));
        return await tcs.Task;
    }
    private static void Complete(long id, nint result)
    {
        if (!Pending.TryRemove(id, out var tcs)) return;
        try { using var json = JsonDocument.Parse(Marshal.PtrToStringUTF8(result) ?? "{}"); if (json.RootElement.TryGetProperty("error", out var error))  { if (error.GetString() == "翻译已取消") tcs.TrySetCanceled(); else tcs.TrySetException(new InvalidOperationException(error.GetString())); } else tcs.TrySetResult(json.RootElement.GetProperty("text").EnumerateArray().Select(x => x.GetString() ?? "").ToArray()); }
        catch (Exception e) { tcs.TrySetException(e); }
    }
}
public sealed class AppleTranslation : ITranslationService { public Task<string[]> TranslateAsync(string[] text, string target, CancellationToken ct) => Native.TranslateAsync(text, target, ct); }
public sealed class NativeVideo : NativeControlHost
{
    public string Url { get; }
    public NativeVideo(string url) => Url = url;
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent) => OperatingSystem.IsMacOS() ? new PlatformHandle(Native.hr_video_create(Url), "NSView") : base.CreateNativeControlCore(parent);
    protected override void DestroyNativeControlCore(IPlatformHandle control) { if (OperatingSystem.IsMacOS()) Native.hr_video_release(control.Handle); else base.DestroyNativeControlCore(control); }
}

public sealed class NativeGif(string path) : NativeControlHost
{
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent) => new PlatformHandle(Native.hr_gif_create(path), "NSView");
    protected override void DestroyNativeControlCore(IPlatformHandle control) => Native.hr_gif_release(control.Handle);
}
