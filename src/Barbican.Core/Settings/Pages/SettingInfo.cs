// Mirrors: see ISettingInfo; Bulwark components/settings/account-settings.tsx (email, username, authentication, server, storage, account type, addresses), account-security-settings.tsx (two-step state), about-data-settings.tsx; Rust settings.rs (the info rows of the schema were dropped by schema_rows: G-069, B-SET-030).
// Every keyless row of kind Info is answered here: the rows that show state (S-keyless-001..007, 009, 011) from the active account, the others (the cross-links of the accessibility page, the buttons' captions) with the row's own description. D-M6-A01 ids.

using System.Reflection;
using Barbican.Core.Accounts;
using Barbican.Core.Settings.Account;

namespace Barbican.Core.Settings.Pages;

/// <summary>The default <see cref="ISettingInfo"/>.</summary>
public sealed class SettingInfo(IAccountManager accounts, IAccountQuota quota, IAccountSecurity security) : ISettingInfo
{
    private const string AppName = "Barbican";

    /// <inheritdoc/>
    public bool Provides(string rowId) => SettingsCatalog.RowOf(rowId) is { Status: SettingRowStatus.Keyless, Kind: SettingRowKind.Info };

    /// <inheritdoc/>
    public Task<string> TextAsync(string rowId, CancellationToken ct) => Task.Run(() => TextCoreAsync(rowId, ct), ct);

#pragma warning disable MA0051 // Each catalog information row maps to its own account query.
    private async Task<string> TextCoreAsync(string rowId, CancellationToken ct)
    {
        if (!Provides(rowId))
        {
            return string.Empty;
        }

        var key = accounts.Active?.Key;
        switch (rowId)
        {
            case "S-keyless-001":
            case "S-keyless-002":
                return accounts.Active?.User ?? string.Empty;
            case "S-keyless-003":
                return accounts.Active is { } a ? (string.Equals(a.Kind, StoredAccount.PasswordKind, StringComparison.Ordinal) ? "Password" : "OAuth") : string.Empty;
            case "S-keyless-004":
                return accounts.Active?.Server ?? string.Empty;
            case "S-keyless-005":
            {
                if (key is null)
                {
                    return string.Empty;
                }

                var usage = await quota.UsageAsync(key, ct).ConfigureAwait(false);
                return usage.IsOk ? usage.Value!.Text : string.Empty;
            }

            case "S-keyless-006":
                return accounts.Active is null ? string.Empty : (accounts.SessionFor(accounts.Active.Key)?.Client.Session.IsStalwart == true ? "Stalwart mail server" : "JMAP mail server");
            case "S-keyless-007":
            {
                if (key is null || !security.IsAvailable(key))
                {
                    return string.Empty;
                }

                var name = await security.DisplayNameAsync(key, ct).ConfigureAwait(false);
                return name.IsOk ? name.Value ?? string.Empty : string.Empty;
            }

            case "S-keyless-009":
            {
                if (key is null || accounts.SessionFor(key) is not { } s)
                {
                    return string.Empty;
                }

                var ids = await s.Mail.GetIdentitiesAsync(ct).ConfigureAwait(false);
                return ids.IsOk ? string.Join(", ", ids.Value!.Select(i => i.Email)) : string.Empty;
            }

            case "S-keyless-011":
            {
                if (key is null || !security.IsAvailable(key))
                {
                    return "Not available on this server";
                }

                var totp = await security.TotpAsync(key, ct).ConfigureAwait(false);
                return totp.IsOk ? (totp.Value!.Enabled ? "On" : "Off") : string.Empty;
            }

            default:
                return (SettingsCatalog.RowOf(rowId)?.Description ?? string.Empty).Replace("{appName}", AppName, StringComparison.Ordinal);
        }
    }

#pragma warning restore MA0051
    /// <summary>The version the About page shows (the informational version of the app, without a build suffix).</summary>
    public static string Version()
    {
        var v = typeof(SettingInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? typeof(SettingInfo).Assembly.GetName().Version?.ToString() ?? string.Empty;
        var plus = v.IndexOf('+', StringComparison.Ordinal);
        return plus >= 0 ? v[..plus] : v;
    }
}
