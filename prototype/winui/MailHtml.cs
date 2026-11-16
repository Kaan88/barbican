using System.Net;

namespace Barbican;

/// Wraps a message body in a page whose background matches the reader tile, so the WebView
/// never shows a colour the native UI does not (same idea as desktop/src/mailview.rs).
public static class MailHtml
{
    const string Js = """
document.addEventListener('contextmenu',function(e){e.preventDefault();chrome.webview.postMessage(JSON.stringify({t:'ctx',x:e.clientX,y:e.clientY}))});
document.addEventListener('keydown',function(e){if(e.ctrlKey||e.altKey||e.metaKey)return;var k=e.key.toLowerCase();if('jkts'.indexOf(k)>=0&&k.length==1){e.preventDefault();chrome.webview.postMessage(JSON.stringify({t:'key',k:k}))}});
document.addEventListener('click',function(e){var a=e.target.closest('a');if(a)e.preventDefault()});
""";

    public static string Wrap(Mail m)
    {
        string bg = Palette.Css("Tile"), fg = Palette.Css("Body"), accent = Palette.Css("Accent"), track = Palette.Css("Track");
        string scheme = Palette.Dark ? "dark" : "light";
        string content = m.Html ?? $"<pre class=plain>{WebUtility.HtmlEncode(m.Plain ?? "")}</pre>";
        return "<!doctype html><html style=\"background:" + bg + ";color-scheme:" + scheme + "\"><head><meta charset=utf-8><style>" +
            "html,body{margin:0;background:" + bg + ";color:" + fg + ";font:15px/1.6 'Segoe UI',system-ui,sans-serif}" +
            (Palette.Nordic
                ? "body{padding:0 44px 24px;font-size:16px;line-height:1.8}body>.c{max-width:600px}p{margin:0 0 14px}"
                : "body{padding:6px 20px 28px}") +
            "a{color:" + accent + "}img{max-width:100%}" +
            "pre.plain{white-space:pre-wrap;font:inherit;margin:0}" +
            "::-webkit-scrollbar{width:12px}::-webkit-scrollbar-thumb{background:" + track + ";border-radius:12px;border:3px solid " + bg + "}::-webkit-scrollbar-track{background:" + bg + "}" +
            "</style></head><body>" + (Palette.Nordic && m.Html != Sample.NewsletterHtml ? "<div class=c>" + content + "</div>" : content) + "<script>" + Js + "</script></body></html>";
    }
}
