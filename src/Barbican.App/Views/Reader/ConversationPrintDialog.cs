// Bulwark components/email/conversation-print.ts: print one message, with its own sender, recipients, subject and body.
using Barbican.App.Composition;
using Barbican.App.Shell;
using Barbican.App.Views.Dialogs;
using Barbican.Core.Mail.Body;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Mail;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Reader;

internal sealed class ConversationPrintDialog : IDialogContent
{
    public bool OwnButtons => true;
    public double MaxWidth => 680;
    public bool CanDraw(object request) => request is ConversationPrintRequest;

    public UIElement Create(ActiveDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var card = ((ConversationPrintRequest)dialog.Request).Card;
        var body = card.Body!;
        var content = PrintText(body);
        var layout = new StackPanel { Spacing = 8, Width = 480, MaxWidth = 540 };
        // Only escaped text enters this print document. The browser paginates long messages.
        var web = new WebView2 { Height = 360 };
        AutomationProperties.SetAutomationId(web, "reader.thread.print.preview." + body.Id);
        AutomationProperties.SetName(web, "Print preview for " + body.Subject);
        var print = DialogStyles.FooterButton("Print", Barbican.Presentation.Compose.DialogTone.Primary, "reader.thread.print.confirm", () => { }, false);
        AutomationProperties.SetAutomationId(print, "reader.thread.print.confirm");
        AutomationProperties.SetName(print, "Print message");
        var close = DialogStyles.FooterButton("Close", Barbican.Presentation.Compose.DialogTone.Plain, "reader.thread.print.close", () => { });
        AutomationProperties.SetAutomationId(close, "reader.thread.print.close");
        AutomationProperties.SetName(close, "Close print preview");
        var reader = ViewHost.Get<ReaderViewModel>();
        var active = ViewHost.Get<IActiveMail>();
        bool Owned() => reader.Owns(card) && string.Equals(active.AccountKey, card.AccountKey, StringComparison.Ordinal);
        var completed = false;
        void Close()
        {
            if (completed) return;
            completed = true;
            dialog.Complete(false);
        }

        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (!Owned()) Close();
        }

        reader.PropertyChanged += Changed;
        web.Loaded += (_, _) => _ = LoadPreviewAsync(web, layout, print, content, Owned);
        web.Unloaded += (_, _) =>
        {
            reader.PropertyChanged -= Changed;
#if WINDOWS
            web.Close();
#endif
        };
        close.Click += (_, _) => Close();
#if WINDOWS
        print.Click += (_, _) =>
        {
            if (Owned()) web.CoreWebView2?.ShowPrintUI(Microsoft.Web.WebView2.Core.CoreWebView2PrintDialogKind.Browser);
        };
#endif
        layout.Children.Add(web);
        layout.Children.Add(DialogStyles.Footer(close, print));
        return layout;
    }

    private static string PrintText(Barbican.Core.Mail.Model.EmailBody body)
    {
        var (html, text) = MailHtml.BodyText(body);
        var plain = string.IsNullOrWhiteSpace(text) ? MailHtml.HtmlToText(html) : text;
        var from = string.Join(", ", body.From.Select(a => a.Email));
        var to = string.Join(", ", body.To.Select(a => a.Email));
        return $"{body.Subject}\nFrom: {from}\nTo: {to}\n{body.ReceivedAt:g}\n\n{plain}";
    }

    private static async Task LoadPreviewAsync(WebView2 web, StackPanel layout, Button print, string content, Func<bool> owned)
    {
        try
        {
            await MailWebViewStartup.EnsureAsync(web, ViewHost.Get<HiddenWindowOptions>().IsHidden);
            if (!web.IsLoaded || !owned()) return;
#if WINDOWS
            // Message text crosses as data. The small fixed document also handles messages beyond NavigateToString's limit.
            var navigated = false;
            web.NavigationStarting += (_, args) =>
            {
                // NavigateToString starts the only permitted navigation, before any document is interactive.
                args.Cancel = navigated;
                navigated = true;
            };
            web.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
            web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            web.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            web.CoreWebView2.WebMessageReceived += (_, args) =>
            {
                if (string.Equals(args.TryGetWebMessageAsString(), "ready", StringComparison.Ordinal) && owned()) print.IsEnabled = true;
            };
            web.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess && owned()) web.CoreWebView2.PostWebMessageAsString(content);
            };
            web.NavigateToString("""
                <!doctype html><meta charset=utf-8><meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-print'">
                <style>html,body{background:white;color:black;color-scheme:light}pre{font:14px/1.5 sans-serif;white-space:pre-wrap;overflow-wrap:anywhere}</style><pre></pre>
                <script nonce="print">chrome.webview.addEventListener('message', e => { document.querySelector('pre').textContent = e.data; chrome.webview.postMessage('ready'); });</script>
                """);
#else
            web.NavigateToString("<!doctype html><meta charset=utf-8><pre>" + MailHtml.Escape(content) + "</pre>");
#endif
        }
#pragma warning disable CA1031 // The print preview reports engine startup failures in its own dialog.
        catch (Exception)
#pragma warning restore CA1031
        {
            if (web.IsLoaded && owned()) layout.Children.Insert(0, new TextBlock { Text = "Print preview unavailable." });
        }
    }
}
