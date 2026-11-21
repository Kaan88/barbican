// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:1371-1411 downloadFile and safeDownloadName (ui:download-file, host-api.ts:233; {content: string or bytes, filename, contentType?}; the name loses path separators, control and bidi
// characters and leading/trailing dots and spaces, at most 120 characters, "download" when empty; programs, scripts and active documents are refused by extension and by type).
// Deviations (Barbican writes to disk instead of handing a browser a blob): the USER picks the place in the platform's own save dialog and the add-on never names a path; at most 5 saves a minute and one dialog open at a time;
// content up to 25 MiB; filename must be a string of at most 255 characters and contentType a string when given (Bulwark coerces); the blocked extensions also list dll, reg, pif, cpl, msc, wsh, wsc, appx, msix and jnlp;
// there is no organisation device policy to consult (host-api.ts:1409 downloadAllowed). Closing the dialog resolves undefined like a saved file does (the add-on cannot tell), and nothing is written.
using System.Text;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Navigation;

internal sealed class PluginUiDownloadFile(PluginNavigationHost host) : PluginNavigationMethod("ui.downloadFile", [["ui:download-file"]], host, mutates: true, deadline: TimeSpan.FromMinutes(10))
{
    internal const int MaxBytes = 25 * 1024 * 1024;
    private static readonly string[] Keys = ["content", "filename", "contentType"];

    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.Ordinal)
    {
        "exe", "msi", "bat", "cmd", "com", "scr", "ps1", "vbs", "vbe", "js", "jse", "wsf", "hta", "jar", "lnk", "app", "dmg", "pkg", "sh", "command",
        "html", "htm", "xhtml", "shtml", "svg", "mhtml", "xml",
        "dll", "reg", "pif", "cpl", "msc", "wsh", "wsc", "appx", "msix", "jnlp",
    };

    protected override string RateKind => "ui-download-file";

    protected override int RateLimit => 5;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments)
    {
        if (!PluginMailArgs.TryOptions(PluginMailArgs.At(arguments, 0), Keys, true, out var options)) return PluginErrorCode.InvalidArguments;
        if (PluginMailArgs.Prop(options, "filename") is not { Kind: PluginValueKind.String, StringValue.Length: <= 255 } name) return PluginErrorCode.InvalidArguments;
        string? type = null;
        if (PluginMailArgs.Prop(options, "contentType") is { } rawType)
        {
            if (!PluginMailArgs.TryString(rawType, 255, out var text)) return PluginErrorCode.InvalidArguments;
            type = text;
        }

        if (!TryContent(PluginMailArgs.Prop(options, "content"), out _)) return PluginErrorCode.InvalidArguments;
        var clean = SafeName(name.StringValue);
        var dot = clean.LastIndexOf('.');
        var extension = dot >= 0 ? clean[(dot + 1)..].ToLowerInvariant() : "";
        return BlockedExtensions.Contains(extension) || BlockedType(type) ? PluginErrorCode.InvalidArguments : null;
    }

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        if (Host.Saver is not { } saver) return Fail(PluginErrorCode.Unavailable);
        var options = PluginMailArgs.At(arguments, 0)!;
        _ = TryContent(PluginMailArgs.Prop(options, "content"), out var bytes);
        var name = SafeName(options.Property("filename").StringValue);
        // one dialog at a time: an add-on cannot stack save dialogs on the user
        if (!Host.TryBeginSave()) return Fail(PluginErrorCode.Unavailable);
        try
        {
            var outcome = await saver.SaveAsync(name, bytes, token => BeginWriteAsync(context, token), ct).ConfigureAwait(false);
            return outcome == PluginSaveOutcome.Failed ? Fail(PluginErrorCode.Failed) : Done();
        }
        finally
        {
            Host.EndSave();
        }
    }

    private static bool TryContent(PluginValue? value, out byte[] bytes)
    {
        bytes = [];
        switch (value)
        {
            case { Kind: PluginValueKind.String }:
                if (Encoding.UTF8.GetByteCount(value.StringValue) > MaxBytes) return false;
                bytes = Encoding.UTF8.GetBytes(value.StringValue);
                return true;
            case { Kind: PluginValueKind.View or PluginValueKind.ArrayBuffer or PluginValueKind.Blob, ByteLength: <= MaxBytes }:
                bytes = value.BinaryBytes();
                return true;
            default:
                return false;
        }
    }

    private static bool BlockedType(string? type)
    {
        if (type is null) return false;
        var t = type.Trim();
        return t.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) || t.StartsWith("application/xhtml", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("image/svg", StringComparison.OrdinalIgnoreCase) || t.StartsWith("application/javascript", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("application/x-javascript", StringComparison.OrdinalIgnoreCase) || t.StartsWith("text/javascript", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("application/x-msdownload", StringComparison.OrdinalIgnoreCase) || t.StartsWith("application/xml", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("text/xml", StringComparison.OrdinalIgnoreCase);
    }

    // host-api.ts:1383-1392 safeDownloadName
    internal static string SafeName(string raw)
    {
        var cleaned = string.Concat(raw
            .Where(c => c is > '\u001f' and not ('\u007f' or '‎' or '‏' or >= '‪' and <= '‮' or >= '⁦' and <= '⁩'))
            .Select(c => c is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '_' : c));
        var start = 0;
        var end = cleaned.Length;
        while (start < end && (cleaned[start] == '.' || char.IsWhiteSpace(cleaned[start]))) start++;
        while (end > start && (cleaned[end - 1] == '.' || char.IsWhiteSpace(cleaned[end - 1]))) end--;
        cleaned = cleaned.Substring(start, Math.Min(120, end - start));
        if (cleaned.Length > 0 && char.IsHighSurrogate(cleaned[^1])) cleaned = cleaned[..^1];
        return cleaned.Length == 0 ? "download" : cleaned;
    }
}
