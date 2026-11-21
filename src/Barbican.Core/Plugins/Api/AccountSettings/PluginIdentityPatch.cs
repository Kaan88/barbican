// Mirrors: Bulwark lib/plugin-sandbox/host-api-settings.ts:125-185 doIdentitiesUpdate input: the patch holds only name (at most 256, trimmed), replyTo (null or at most 10 addresses of "a@b.c" or "Name <a@b.c>", each at most 320 characters
// and valid), textSignature (null or at most 20,000; blank clears it) and htmlSignature (null or at most 100,000; blank clears it, otherwise cleaned); it is not empty and never names email (the address of an identity cannot change).
// Barbican's identity page keeps Reply-To as a comma-separated list, so a name that holds a comma cannot be stored and is refused. HTML is cleaned with MailSanitizer (active markup removed); Bulwark's sanitizeSignatureHtml is a
// stricter allowlist that Barbican has no equivalent of.
using Barbican.Core.Mail.Body;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Settings.Account;

namespace Barbican.Core.Plugins.Api.AccountSettings;

internal sealed class PluginIdentityPatch
{
    internal const int MaxName = 256;
    internal const int MaxReplyTo = 10;
    internal const int MaxText = 20_000;
    internal const int MaxHtml = 100_000;
    private static readonly string[] Keys = ["name", "replyTo", "textSignature", "htmlSignature"];

    private PluginIdentityPatch()
    {
    }

    internal string? Name { get; private set; }

    /// <summary>The Reply-To list as the page stores it; empty clears it; null leaves it alone.</summary>
    internal string? ReplyTo { get; private set; }

    internal string? TextSignature { get; private set; }

    /// <summary>The raw HTML, cleaned at <see cref="CleanedHtml"/>; empty clears; null leaves it alone.</summary>
    internal string? HtmlSignature { get; private set; }

    internal static PluginIdentityPatch? TryRead(PluginValue? patch)
    {
        if (!PluginMailArgs.TryOptions(patch, Keys, true, out var data) || data is null || data.PropertyNames.Count == 0) return null;
        var result = new PluginIdentityPatch();
        if (PluginMailArgs.Prop(data, "name") is { } name)
        {
            if (!PluginMailArgs.TryString(name, int.MaxValue, out var text) || text.Trim().Length > MaxName) return null;
            result.Name = text.Trim();
        }

        if (PluginMailArgs.Prop(data, "replyTo") is { } replyTo && !TryReplyTo(replyTo, result)) return null;
        if (PluginMailArgs.Prop(data, "textSignature") is { } textSignature)
        {
            if (textSignature.Kind == PluginValueKind.Null) result.TextSignature = "";
            else if (!PluginMailArgs.TryString(textSignature, MaxText, out var text)) return null;
            else result.TextSignature = text.Trim().Length == 0 ? "" : text;
        }

        if (PluginMailArgs.Prop(data, "htmlSignature") is { } htmlSignature)
        {
            if (htmlSignature.Kind == PluginValueKind.Null) result.HtmlSignature = "";
            else if (!PluginMailArgs.TryString(htmlSignature, MaxHtml, out var html)) return null;
            else result.HtmlSignature = html.Trim().Length == 0 ? "" : html;
        }

        return result;
    }

    /// <summary>The HTML signature cleaned of active markup; empty stays empty. Parses the markup: run it off the UI thread.</summary>
    internal string? CleanedHtml() => string.IsNullOrEmpty(HtmlSignature) ? HtmlSignature : MailSanitizer.Clean(HtmlSignature, out _);

    private static bool TryReplyTo(PluginValue value, PluginIdentityPatch result)
    {
        if (value.Kind == PluginValueKind.Null)
        {
            result.ReplyTo = "";
            return true;
        }

        if (value.Kind != PluginValueKind.Array || value.Count > MaxReplyTo) return false;
        var list = new List<string>();
        for (var i = 0; i < value.Count; i++)
        {
            if (!PluginMailArgs.TryString(value.Item(i), 320, out var entry) || entry.Trim() is not { Length: > 0 } text || text.Any(c => c is '\r' or '\n' or ',')) return false;
            list.Add(text);
        }

        var joined = string.Join(", ", list);
        if (list.Count > 0 && (AccountIdentityRules.ParseList(joined).Count != list.Count
            || AccountIdentityRules.Problem(new AccountIdentityEdit(null, null, joined, null, null, null), isCreate: false) is not null)) return false;
        result.ReplyTo = joined;
        return true;
    }
}
