// Mirrors: Bulwark lib/plugin-sandbox/host-api-input.ts:46-182 (readObject, readId, readString, readAddressList, readBlobRefs: unknown properties are refused, wrong types are not coerced),
// host-api-mail.ts:255-264 readIdList (at most 100 distinct ids), assistant/c.batch.ts:20-47 (at most 200) and host-api.ts:1421-1446 (assertPluginKeyword: RFC 5788 charset, server-set and protected keywords).
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.MailActions;

internal static class PluginMailActionArgs
{
    internal const int MaxMoveIds = 100;
    internal const int MaxBatchIds = 200;
    internal const int MaxKeywordChanges = 20;

    /// <summary>Keywords that change what the app and server do with a message, not just how it is labelled (host-api.ts:1439): the add-on methods leave them to the app's own actions.</summary>
    private static readonly string[] Protected = ["$draft", "$junk", "$notjunk", "$phishing"];

    /// <summary>A non-empty array of ids, each 1 to 256 characters, at most <paramref name="max"/> distinct ones (a repeated id counts once).</summary>
    internal static bool TryIdList(PluginValue? value, int max, out IReadOnlyList<string> ids)
    {
        ids = [];
        if (value is not { Kind: PluginValueKind.Array } || value.Count == 0 || value.Count > max) return false;
        var list = new List<string>();
        for (var i = 0; i < value.Count; i++)
        {
            var item = value.Item(i);
            if (!PluginMailArgs.IsId(item)) return false;
            if (!list.Contains(item.StringValue, StringComparer.Ordinal)) list.Add(item.StringValue);
        }

        ids = list;
        return true;
    }

    /// <summary>The RFC 5788 charset of host-api.ts:1421-1430 and not one the server sets itself; with <paramref name="mailMethod"/> also not a protected one.</summary>
    internal static bool IsPluginKeyword(string keyword, bool mailMethod)
    {
        if (keyword.Length is 0 or > 128 || !(char.IsAsciiLetterOrDigit(keyword[0]) || keyword[0] == '$')) return false;
        foreach (var c in keyword.AsSpan(1))
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '$' or '_' or '.' or ':' or '-')) return false;
        }

        if (PluginMailProjection.IsServerSet(keyword)) return false;
        return !mailMethod || !Protected.Contains(keyword, StringComparer.OrdinalIgnoreCase);
    }

    internal static bool TryKeyword(PluginValue? value, bool mailMethod, out string keyword)
    {
        keyword = "";
        if (value is not { Kind: PluginValueKind.String } || !IsPluginKeyword(value.StringValue, mailMethod)) return false;
        keyword = value.StringValue;
        return true;
    }

    /// <summary>host-api-input.ts:105-125: an address or <c>Name &lt;address&gt;</c>, at most 320 characters, no line breaks.</summary>
    internal static bool TryAddressList(PluginValue? value, int max, out IReadOnlyList<string> addresses)
    {
        addresses = [];
        if (value is null) return true;
        if (value.Kind != PluginValueKind.Array || value.Count > max) return false;
        var list = new List<string>();
        for (var i = 0; i < value.Count; i++)
        {
            var item = value.Item(i);
            if (item.Kind != PluginValueKind.String || item.StringValue.Length > 320) return false;
            var trimmed = item.StringValue.Trim();
            var address = AddressOf(trimmed);
            if (!IsValidAddress(address) || trimmed.Contains('\r', StringComparison.Ordinal) || trimmed.Contains('\n', StringComparison.Ordinal)) return false;
            list.Add(trimmed);
        }

        addresses = list;
        return true;
    }

    /// <summary>The address part of <c>Name &lt;addr&gt;</c> (or the address itself).</summary>
    internal static string AddressOf(string entry)
    {
        var open = entry.LastIndexOf('<');
        return open >= 0 && entry.EndsWith('>') && open < entry.Length - 1 ? entry[(open + 1)..^1] : entry;
    }

    private static bool IsValidAddress(string address)
    {
        var at = address.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || at != address.LastIndexOf('@') || at == address.Length - 1 || address.Length > 254) return false;
        foreach (var c in address)
        {
            if (char.IsWhiteSpace(c) || c is '<' or '>' or ',' or ';' or '"' or '(' or ')' or '[' or ']' or '\\' || char.IsControl(c)) return false;
        }

        var domain = address[(at + 1)..];
        return domain.Contains('.', StringComparison.Ordinal) && !domain.StartsWith('.') && !domain.EndsWith('.') && !domain.Contains("..", StringComparison.Ordinal);
    }

    /// <summary>host-api-input.ts:140-155: a media type without parameters.</summary>
    internal static bool IsMediaType(string value)
    {
        var slash = value.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0 || slash == value.Length - 1 || slash != value.LastIndexOf('/') || value.Length > 255) return false;
        return Token(value.AsSpan(0, slash)) && Token(value.AsSpan(slash + 1));

        static bool Token(ReadOnlySpan<char> part)
        {
            if (part.Length is 0 or > 127 || !char.IsAsciiLetterOrDigit(part[0])) return false;
            foreach (var c in part)
            {
                if (!(char.IsAsciiLetterOrDigit(c) || "!#$&^_.+-".Contains(c, StringComparison.Ordinal))) return false;
            }

            return true;
        }
    }
}
