#if WINDOWS
// Host-owned, offline WebView used to turn sanitized add-on HTML into an in-memory PDF.
using Barbican.App.Composition;
using Barbican.App.Shell;
using Barbican.Core.Mail.Body;
using Microsoft.Web.WebView2.Core;
using Windows.Storage.Streams;

namespace Barbican.App.Views.Plugins;

internal static class PluginHtmlPrintPreparation
{
    private const int MaxHtml = 2 * 1024 * 1024;
    private const string DocumentUri = "https://print.invalid/document";
    private const ulong MaxPdf = 25 * 1024 * 1024;

    internal static async Task<byte[]?> PrepareAsync(string html, CancellationToken ct)
    {
        if (html.Length is 0 or > MaxHtml || ActiveWindow.Current?.Content is not Page { Content: Panel root }) return null;
        var clean = MailSanitizer.Clean(html, out _);
        if (clean.Length > MaxHtml) return null;
        using var content = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(content))
        {
            writer.WriteBytes(System.Text.Encoding.UTF8.GetBytes("<!doctype html><html><head><meta charset=utf-8></head><body>" + clean + "</body></html>"));
            _ = await writer.StoreAsync().AsTask(ct).ConfigureAwait(true);
            _ = writer.DetachStream();
        }
        content.Seek(0);
        var web = new WebView2 { Width = 816, Height = 1056, Opacity = 0, IsHitTestVisible = false };
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        web.Loaded += (_, _) => loaded.TrySetResult();
        root.Children.Add(web);
        try
        {
            if (!web.IsLoaded) await loaded.Task.WaitAsync(ct).ConfigureAwait(true);
            await MailWebViewStartup.EnsureAsync(web, ViewHost.Get<HiddenWindowOptions>().IsHidden).WaitAsync(ct).ConfigureAwait(true);
            return await NavigateAndPrintAsync(web, content, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or System.Runtime.InteropServices.COMException) { return null; }
        finally { root.Children.Remove(web); web.Close(); }
    }
    private static void ConfigureWebView(WebView2 web, InMemoryRandomAccessStream content)
    {
        var core = web.CoreWebView2;
        core.Settings.IsScriptEnabled = false;
        core.Settings.IsWebMessageEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        core.WebResourceRequested += (_, e) =>
        {
            var response = core.Environment.CreateWebResourceResponse(null, 403, "Blocked", "Cache-Control: no-store");
            try
            {
                if (e.ResourceContext == CoreWebView2WebResourceContext.Document &&
                    string.Equals(e.Request.Method, "GET", StringComparison.Ordinal) && string.Equals(e.Request.Uri, DocumentUri, StringComparison.Ordinal))
                {
                    response = core.Environment.CreateWebResourceResponse(content.CloneStream(), 200, "OK",
                        "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nContent-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; img-src data:; font-src data:; form-action 'none'; base-uri 'none'\r\n");
                }
            }
#pragma warning disable CA1031 // Always respond locally even if creating a response fails.
            catch (Exception) { }
#pragma warning restore CA1031
            finally { e.Response = response; }
        };
    }

    private static async Task<byte[]?> NavigateAndPrintAsync(WebView2 web, InMemoryRandomAccessStream content, CancellationToken ct)
    {
        ConfigureWebView(web, content);
        var core = web.CoreWebView2;
        var navigated = false;
        web.NavigationStarting += (_, e) =>
        {
            e.Cancel = navigated || !string.Equals(e.Uri, DocumentUri, StringComparison.Ordinal);
            navigated = true;
        };
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        web.NavigationCompleted += (_, e) => finished.TrySetResult(e.IsSuccess);
        core.Navigate(DocumentUri);
        if (!await finished.Task.WaitAsync(ct).ConfigureAwait(true)) return null;
        using var pdf = await core.PrintToPdfStreamAsync(null).AsTask(ct).ConfigureAwait(true);
        if (pdf is null || pdf.Size is 0 or > MaxPdf) return null;
        pdf.Seek(0);
        using var reader = new DataReader(pdf);
        var size = checked((uint)pdf.Size);
        if (await reader.LoadAsync(size).AsTask(ct).ConfigureAwait(true) != size) return null;
        var bytes = new byte[size];
        reader.ReadBytes(bytes);
        return bytes;
    }
}
#endif
