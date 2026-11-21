// Mirrors: Bulwark lib/plugin-sandbox/pages.ts:19-45 (MAX_PAGES_PER_PLUGIN 3, ID_RE, ICON_RE, title 1-40 characters after trim, duplicate ids refused) and page-navigation.ts:14-37 (path at most 2000 characters, no "." or ".." segment).
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Pages;

/// <summary>The limits of add-on pages, and the checks of the two API arguments.</summary>
public static class PluginPageChecks
{
    public const int MaxPagesPerAddOn = 3;
    public const int MaxTitleLength = 40;
    public const int MaxIdLength = 32;
    public const int MaxIconLength = 40;
    public const int MaxPathLength = 2000;

    /// <summary>Whether <paramref name="id"/> is a page id: <c>[a-z0-9][a-z0-9-]{0,31}</c>.</summary>
    public static bool IsPageId(string id)
    {
        if (id.Length is 0 or > MaxIdLength) return false;
        for (var i = 0; i < id.Length; i++)
        {
            var c = id[i];
            var ok = c is (>= 'a' and <= 'z') or (>= '0' and <= '9') || (i > 0 && c == '-');
            if (!ok) return false;
        }
        return true;
    }

    /// <summary>Whether <paramref name="icon"/> is an icon name: <c>[A-Za-z0-9-]{1,40}</c>.</summary>
    public static bool IsIconName(string icon) =>
        icon.Length is > 0 and <= MaxIconLength && icon.All(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-');

    /// <summary>The page list of <c>ui.setPages</c>; false for anything Bulwark's checkPages throws on (the call is InvalidArguments).</summary>
    public static bool TryRead(PluginValue? list, out IReadOnlyList<PluginPage> pages)
    {
        pages = [];
        if (list is not { Kind: PluginValueKind.Array } || list.Count > MaxPagesPerAddOn) return false;
        var result = new List<PluginPage>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < list.Count; i++)
        {
            if (!PluginMailArgs.TryOptions(list.Item(i), ["id", "title", "icon"], true, out var page)) return false;
            if (PluginMailArgs.Prop(page, "id") is not { Kind: PluginValueKind.String } id || !IsPageId(id.StringValue) || !seen.Add(id.StringValue)) return false;
            if (PluginMailArgs.Prop(page, "title") is not { Kind: PluginValueKind.String } title) return false;
            var text = title.StringValue.Trim();
            if (text.Length == 0 || title.StringValue.Length > MaxTitleLength) return false;
            string? icon = null;
            if (PluginMailArgs.Prop(page, "icon") is { } iconValue)
            {
                if (iconValue.Kind != PluginValueKind.String || !IsIconName(iconValue.StringValue)) return false;
                icon = iconValue.StringValue;
            }
            result.Add(new PluginPage(id.StringValue, text, icon));
        }
        pages = result;
        return true;
    }

    /// <summary>The optional path of <c>ui.openPage</c>: absent, null or empty is no path; a string up to 2000 characters without "." or ".." segments.</summary>
    public static bool TryPath(PluginValue? value, out string path)
    {
        path = "";
        if (value is null or { Kind: PluginValueKind.Null }) return true;
        if (value.Kind != PluginValueKind.String || value.StringValue.Length > MaxPathLength) return false;
        var text = value.StringValue.TrimStart('/');
        if (text.Split('/').Any(segment => segment is "." or "..")) return false;
        path = text;
        return true;
    }
}
