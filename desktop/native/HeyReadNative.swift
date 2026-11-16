import AppKit
import AVKit
import SwiftUI
import Translation
import Security
import CoreText
import WebKit

public typealias Callback = @convention(c) (Int64, UnsafePointer<CChar>?) -> Void
@_cdecl("hr_free") public func hrFree(_ p: UnsafeMutableRawPointer?) { free(p) }
@_cdecl("hr_keychain_read") public func keychainRead(_ service: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>? {
    let q: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: String(cString: service), kSecAttrAccount as String: "session", kSecReturnData as String: true, kSecMatchLimit as String: kSecMatchLimitOne]
    var item: CFTypeRef?; guard SecItemCopyMatching(q as CFDictionary, &item) == errSecSuccess, let data = item as? Data, let value = String(data: data, encoding: .utf8) else { return nil }; return strdup(value)
}
@_cdecl("hr_keychain_write") public func keychainWrite(_ service: UnsafePointer<CChar>, _ value: UnsafePointer<CChar>?) -> Int32 {
    let q: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: String(cString: service), kSecAttrAccount as String: "session"]
    guard let value else { let status = SecItemDelete(q as CFDictionary); return status == errSecItemNotFound ? 0 : status }
    let data = Data(String(cString: value).utf8)
    let update = SecItemUpdate(q as CFDictionary, [kSecValueData as String: data] as CFDictionary)
    if update != errSecItemNotFound { return update }
    var add = q; add[kSecValueData as String] = data; add[kSecAttrAccessible as String] = kSecAttrAccessibleWhenUnlockedThisDeviceOnly
    return SecItemAdd(add as CFDictionary, nil)
}
@_cdecl("hr_font_register") public func registerFont(_ path: UnsafePointer<CChar>) -> UnsafeMutablePointer<CChar>? {
    let url = URL(fileURLWithPath: String(cString: path)); CTFontManagerRegisterFontsForURL(url as CFURL, .process, nil)
    guard let descriptors = CTFontManagerCreateFontDescriptorsFromURL(url as CFURL) as? [CTFontDescriptor], let first = descriptors.first, let name = CTFontDescriptorCopyAttribute(first, kCTFontFamilyNameAttribute) as? String else { return nil }; return strdup(name)
}
@_cdecl("hr_accent") public func accent() -> UnsafeMutablePointer<CChar>? {
    guard let c = NSColor.controlAccentColor.usingColorSpace(.deviceRGB) else { return nil }
    return strdup(String(format: "#%02X%02X%02X", Int(c.redComponent * 255), Int(c.greenComponent * 255), Int(c.blueComponent * 255)))
}
@_cdecl("hr_video_create") public func videoCreate(_ url: UnsafePointer<CChar>) -> UnsafeMutableRawPointer? {
    guard let url = URL(string: String(cString: url)), url.scheme == "https" else { return nil }
    let view = AVPlayerView(); view.controlsStyle = .inline; view.player = AVPlayer(url: url); return Unmanaged.passRetained(view).toOpaque()
}
@_cdecl("hr_video_release") public func videoRelease(_ p: UnsafeMutableRawPointer?) {
    guard let p else { return }; let view = Unmanaged<AVPlayerView>.fromOpaque(p).takeRetainedValue(); view.player?.pause(); view.player?.replaceCurrentItem(with: nil); view.player = nil; view.removeFromSuperview()
}
@_cdecl("hr_share") public func share(_ value: UnsafePointer<CChar>) {
    let text = String(cString: value); guard let parent = NSApp.keyWindow?.contentView else { return }
    let item: Any = URL(string: text) ?? text as Any
    NSSharingServicePicker(items: [item]).show(relativeTo: NSRect(x: parent.bounds.midX, y: parent.bounds.maxY - 50, width: 1, height: 1), of: parent, preferredEdge: .minY)
}
private var verificationWindows: [NSWindow] = []
@_cdecl("hr_verify") public func verify(_ value: UnsafePointer<CChar>) {
    guard let url = URL(string: String(cString: value)), url.scheme == "https", let host = url.host, host == "www.xiaoheihe.cn" || host == "api.xiaoheihe.cn" else { return }
    let config = WKWebViewConfiguration(); config.websiteDataStore = .nonPersistent()
    let view = WKWebView(frame: .zero, configuration: config); let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 860, height: 640), styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
    window.title = "小黑盒官方验证"; window.contentView = view; window.center(); window.isReleasedWhenClosed = false; window.makeKeyAndOrderFront(nil); view.load(URLRequest(url: url)); verificationWindows.removeAll { !$0.isVisible }; verificationWindows.append(window)
}
private final class TranslationJob: NSObject, NSWindowDelegate {
    let id: Int64; let callback: Callback; var window: NSWindow?; var finished = false
    init(_ id: Int64, _ callback: @escaping Callback) { self.id = id; self.callback = callback }
    func finish(_ result: [String: Any]) {
        guard !finished else { return }; finished = true
        let data = (try? JSONSerialization.data(withJSONObject: result)) ?? Data("{}".utf8)
        String(decoding: data, as: UTF8.self).withCString { callback(id, $0) }
        window?.close(); window = nil; jobs.removeValue(forKey: id)
    }
    func windowWillClose(_ notification: Notification) { if !finished { finish(["error": "翻译已取消"]) } }
}
private var jobs: [Int64: TranslationJob] = [:]
@available(macOS 15.0, *)
private struct TranslationView: View {
    let job: TranslationJob; let strings: [String]; let target: String
    @State private var configuration: TranslationSession.Configuration?
    var body: some View {
        VStack(spacing: 18) {
            Image(systemName: "character.bubble").font(.system(size: 32)).foregroundStyle(.blue)
            Text("Apple 端侧翻译").font(.headline)
            Text("首次使用可能需要下载语言资源。翻译在设备上完成。").multilineTextAlignment(.center)
            ProgressView(); Button("取消") { job.finish(["error": "翻译已取消"]) }
        }.padding(28).frame(width: 390, height: 240)
        .onAppear { configuration = TranslationSession.Configuration(source: Locale.Language(identifier: "en"), target: Locale.Language(identifier: target)) }
        .translationTask(configuration) { session in
            do {
                try await session.prepareTranslation()
                let responses = try await session.translations(from: strings.enumerated().map { .init(sourceText: $0.element, clientIdentifier: String($0.offset)) })
                var output = Array(repeating: "", count: strings.count)
                for response in responses { if let id = response.clientIdentifier, let i = Int(id), output.indices.contains(i) { output[i] = response.targetText } }
                job.finish(["text": output])
            } catch { job.finish(["error": error.localizedDescription]) }
        }
    }
}
@_cdecl("hr_translate") public func translate(_ id: Int64, _ input: UnsafePointer<CChar>, _ callback: @escaping Callback) {
    guard #available(macOS 15.0, *) else { "{\"error\":\"需要 macOS 15 或更新版本\"}".withCString { callback(id, $0) }; return }
    guard let data = String(cString: input).data(using: .utf8), let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any], let strings = json["text"] as? [String] else { return }
    let job = TranslationJob(id, callback); jobs[id] = job
    let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 390, height: 240), styleMask: [.titled, .closable], backing: .buffered, defer: false)
    job.window = window; window.delegate = job; window.isReleasedWhenClosed = false; window.title = "盒阅 · 翻译"; window.contentView = NSHostingView(rootView: TranslationView(job: job, strings: strings, target: json["target"] as? String ?? "zh-Hans")); window.center(); window.makeKeyAndOrderFront(nil)
}
@_cdecl("hr_translate_cancel") public func cancelTranslation(_ id: Int64) { jobs[id]?.finish(["error": "翻译已取消"]) }
@_cdecl("hr_gif_create") public func gifCreate(_ path: UnsafePointer<CChar>) -> UnsafeMutableRawPointer? {
    let view = NSImageView(); view.image = NSImage(contentsOfFile: String(cString: path)); view.imageScaling = .scaleProportionallyUpOrDown; view.animates = true; return Unmanaged.passRetained(view).toOpaque()
}
@_cdecl("hr_gif_release") public func gifRelease(_ p: UnsafeMutableRawPointer?) { guard let p else { return }; let view = Unmanaged<NSImageView>.fromOpaque(p).takeRetainedValue(); view.animates = false; view.image = nil; view.removeFromSuperview() }
@_cdecl("hr_reduce_motion") public func reduceMotion() -> Int32 { NSWorkspace.shared.accessibilityDisplayShouldReduceMotion ? 1 : 0 }
private final class CaptchaJob: NSObject, WKScriptMessageHandler, WKNavigationDelegate, NSWindowDelegate {
    let id: Int64; let callback: Callback; var window: NSWindow?; var view: WKWebView?; var finished = false
    let hosts: Set<String> = ["turing.captcha.qcloud.com", "ca.turing.captcha.qcloud.com", "turing.captcha.gtimg.com", "global.turing.captcha.gtimg.com", "rce.tencentrio.com", "cloudcache.tencentcs.com"]
    init(_ id: Int64, _ callback: @escaping Callback) { self.id = id; self.callback = callback }
    func finish(_ json: String) {
        guard !finished else { return }; finished = true; json.withCString { callback(id, $0) }
        view?.stopLoading(); view?.configuration.userContentController.removeScriptMessageHandler(forName: "HeyReadCaptcha"); view?.navigationDelegate = nil; view = nil; window?.contentView = nil; window?.close(); window = nil; captchaJobs.removeValue(forKey: id)
    }
    func userContentController(_ controller: WKUserContentController, didReceive message: WKScriptMessage) {
        guard message.frameInfo.isMainFrame, message.frameInfo.securityOrigin.host == "www.xiaoheihe.cn", let json = message.body as? String, json.utf8.count <= 16384 else { return }; finish(json)
    }
    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        guard let url = navigationAction.request.url else { decisionHandler(.cancel); return }
        let bootstrap = url.absoluteString == "about:blank" || (url.scheme == "https" && url.host == "www.xiaoheihe.cn" && url.path == "/")
        let allowed = url.scheme == "https" && url.user == nil && (url.port == nil || url.port == 443) && hosts.contains(url.host ?? "")
        decisionHandler(bootstrap || allowed ? .allow : .cancel)
    }
    func windowWillClose(_ notification: Notification) { finish("{\"ret\":2}") }
}
private var captchaJobs: [Int64: CaptchaJob] = [:]
@_cdecl("hr_captcha") public func captcha(_ id: Int64, _ appIdRaw: UnsafePointer<CChar>, _ callback: @escaping Callback) {
    let appId = String(cString: appIdRaw); guard appId.count <= 20 && !appId.isEmpty && appId.allSatisfy({ $0.isASCII && $0.isNumber }) else { "{\"ret\":1}".withCString { callback(id, $0) }; return }
    let job = CaptchaJob(id, callback); captchaJobs[id] = job
    let config = WKWebViewConfiguration(); config.websiteDataStore = .nonPersistent(); config.userContentController.add(job, name: "HeyReadCaptcha"); config.preferences.javaScriptCanOpenWindowsAutomatically = false
    let view = WKWebView(frame: .zero, configuration: config); job.view = view; view.navigationDelegate = job
    let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 540, height: 580), styleMask: [.titled, .closable], backing: .buffered, defer: false); job.window = window; window.delegate = job; window.isReleasedWhenClosed = false; window.contentView = view; window.title = "小黑盒官方人机验证"; window.center(); window.makeKeyAndOrderFront(nil)
    let origins = job.hosts.sorted().map { "https://" + $0 }.joined(separator: " ")
    let html = """
    <!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'unsafe-inline' \(origins); connect-src \(origins); img-src data: \(origins); frame-src \(origins); style-src 'unsafe-inline' \(origins)"></head><body><p id="status">正在加载官方验证组件，请按页面提示完成验证。</p><script>
    (function(){var done=false;function send(r){if(done)return;done=true;window.webkit.messageHandlers.HeyReadCaptcha.postMessage(JSON.stringify(r));}var sdk=document.createElement('script');sdk.src='https://turing.captcha.qcloud.com/TCaptcha.js';sdk.onload=function(){try{var captcha=new TencentCaptcha('\(appId)',function(r){send(r);},{ready:function(){document.getElementById('status').textContent='';}});captcha.show();}catch(e){send({ret:1});}};sdk.onerror=function(){send({ret:1});};document.head.appendChild(sdk);})();
    </script></body></html>
    """
    view.loadHTMLString(html, baseURL: URL(string: "https://www.xiaoheihe.cn/"))
}
@_cdecl("hr_captcha_cancel") public func cancelCaptcha(_ id: Int64) { captchaJobs[id]?.finish("{\"ret\":2}") }
