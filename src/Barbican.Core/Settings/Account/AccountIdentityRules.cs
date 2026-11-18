// Mirrors: see AccountIdentity; Bulwark components/settings/identity-settings.tsx (validation), lib/identity-order.ts and stores/identity-store.ts (order; Barbican keeps its own list, D-M6-A11). Contract from the M6 plan section 4.5; owner set-data.

namespace Barbican.Core.Settings.Account;

/// <summary>The pure rules of the identities page.</summary>
public static class AccountIdentityRules
{
    /// <summary>What is wrong with an identity before it is sent, or null: a name is optional, the email must have one @ with text each side and a dot in the domain (a wildcard local part "*@domain" is allowed), Bcc and Reply-To must each be a comma-separated list of valid addresses.</summary>
    public static string? Problem(AccountIdentityEdit edit, bool isCreate)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (edit.Email is null ? isCreate : !IsAddress(edit.Email.Trim(), allowWildcard: true))
        {
            return edit.Email is null || edit.Email.Trim().Length == 0 ? "Enter the e-mail address of the identity." : "That is not a valid e-mail address.";
        }

        if (edit.ReplyTo is { Length: > 0 } && !AllAddresses(edit.ReplyTo))
        {
            return "Reply-To must be a list of valid e-mail addresses, separated by commas.";
        }

        return edit.Bcc is { Length: > 0 } && !AllAddresses(edit.Bcc) ? "Bcc must be a list of valid e-mail addresses, separated by commas." : null;
    }

    /// <summary>The identities in the order of <c>preferredIds</c>; ids not in the list follow in server order; ids in the list that no longer exist are ignored.</summary>
    public static IReadOnlyList<AccountIdentity> Ordered(IReadOnlyList<AccountIdentity> identities, IReadOnlyList<string> preferredIds)
    {
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(preferredIds);
        var byId = identities.GroupBy(i => i.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var first = preferredIds.Where(byId.ContainsKey).Distinct(StringComparer.Ordinal).Select(id => byId[id]).ToList();
        return [.. first, .. identities.Where(i => !first.Contains(i))];
    }

    /// <summary>The addresses of a comma-separated list as typed: <c>a@b.c</c> or <c>Name &lt;a@b.c&gt;</c>; empty entries are dropped.</summary>
    public static IReadOnlyList<(string? Name, string Email)> ParseList(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var list = new List<(string?, string)>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var open = part.LastIndexOf('<');
            if (open >= 0 && part.EndsWith('>'))
            {
                var name = part[..open].Trim().Trim('"');
                list.Add((name.Length == 0 ? null : name, part[(open + 1)..^1].Trim()));
            }
            else
            {
                list.Add((null, part));
            }
        }

        return list;
    }

    private static bool AllAddresses(string text) =>
        ParseList(text) is { Count: > 0 } list && list.All(a => IsAddress(a.Email, allowWildcard: false));

    private static bool IsAddress(string text, bool allowWildcard)
    {
        var at = text.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || at != text.LastIndexOf('@') || at == text.Length - 1 || text.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var local = text[..at];
        var domain = text[(at + 1)..];
        if (!allowWildcard && local.Contains('*', StringComparison.Ordinal))
        {
            return false;
        }

        return domain.Contains('.', StringComparison.Ordinal) && !domain.StartsWith('.') && !domain.EndsWith('.') && !domain.Contains("..", StringComparison.Ordinal);
    }
}
