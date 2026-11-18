// Mirrors: see IAccountSecurity; Bulwark stores/account-security-store.ts (fetchAuthInfo :395, changePassword :644, updateDisplayName :677, enableTotp :705, disableTotp :741, createCredential :226, removeCredential :272, updateEncryptionAtRest :500), lib/stalwart/management-capability.ts;
// Stalwart crates/jmap/src/registry/mapping/ (account_password, app_password, api_key), crates/registry/src/schema/structs.rs (AccountPassword.otpAuth, EncryptionAtRest). D-M6-A09: called with the user's own credentials, never logged, never written down; writes run against FakeJmap only (D-M6-09).
// A password, a TOTP secret or an app-password secret is held in memory only for the call (the pending enrolment for the few seconds the user types the code).

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;

namespace Barbican.Core.Settings.Account;

/// <summary>The default <see cref="IAccountSecurity"/>.</summary>
public sealed class AccountSecurity(IAccountManager accounts, IHookBus hooks) : IAccountSecurity
{
    private const string Singleton = "singleton";
    private const string Issuer = "Barbican";

    private readonly Lock _pendingLock = new();
    private readonly Dictionary<string, (string Password, string Uri)> _pending = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public bool IsAvailable(string accountKey) =>
        accounts.SessionFor(accountKey) is { } s && s.Client.Session.Has(JmapCapabilities.Stalwart);

    /// <inheritdoc/>
    public Task<Result<Unit>> ChangePasswordAsync(string accountKey, string current, string newPassword, CancellationToken ct) => Task.Run(
        async () =>
        {
            if (AccountSecurityRules.PasswordProblem(current, newPassword, newPassword) is { } problem)
            {
                return Result<Unit>.Fail(new ValidationError(problem));
            }

            var r = await UpdateSingletonAsync(accountKey, "x:AccountPassword", new JsonObject { ["currentSecret"] = current, ["secret"] = newPassword }, "The password was not changed.", ct).ConfigureAwait(false);
            if (r.IsOk)
            {
                hooks.Fire(HookNames.OnPasswordChange, () => new EmptyPayload());
            }

            return r;
        },
        ct);

    /// <inheritdoc/>
    public Task<Result<AccountTotp>> TotpAsync(string accountKey, CancellationToken ct) => Task.Run(
        async () =>
        {
            var got = await GetSingletonAsync(accountKey, "x:AccountPassword", ct).ConfigureAwait(false);
            if (!got.IsOk)
            {
                return Result<AccountTotp>.Fail(got.Error!);
            }

            var url = got.Value!["otpAuth"].Str("otpUrl");
            return Result<AccountTotp>.Ok(new AccountTotp(!string.IsNullOrEmpty(url), null));
        },
        ct);

    /// <inheritdoc/>
    public Task<Result<AccountTotp>> BeginTotpAsync(string accountKey, string password, CancellationToken ct) => Task.Run(
        () =>
        {
            var s = AccountCalls.Session(accounts, accountKey);
            if (!s.IsOk)
            {
                return Result<AccountTotp>.Fail(s.Error!);
            }

            if (string.IsNullOrEmpty(password))
            {
                return Result<AccountTotp>.Fail(new ValidationError("Enter your current password."));
            }

            var uri = NewOtpUri(s.Value!.Client.Session.Username);
            lock (_pendingLock)
            {
                _pending[accountKey] = (password, uri);
            }

            return Result<AccountTotp>.Ok(new AccountTotp(false, uri));
        },
        ct);

    /// <inheritdoc/>
    public Task<Result<Unit>> ConfirmTotpAsync(string accountKey, string code, CancellationToken ct) => Task.Run(
        async () =>
        {
            if (!AccountSecurityRules.IsTotpCode(code))
            {
                return Result<Unit>.Fail(new ValidationError("Enter the six digits of the code."));
            }

            (string Password, string Uri) pending;
            lock (_pendingLock)
            {
                if (!_pending.TryGetValue(accountKey, out pending))
                {
                    return Result<Unit>.Fail(new ValidationError("Start again: there is no two-step sign-in being set up."));
                }
            }

            var patch = new JsonObject
            {
                ["currentSecret"] = pending.Password,
                ["otpAuth"] = new JsonObject { ["otpUrl"] = pending.Uri, ["otpCode"] = code.Replace(" ", string.Empty, StringComparison.Ordinal) },
            };
            var r = await UpdateSingletonAsync(accountKey, "x:AccountPassword", patch, "Two-step sign-in was not turned on.", ct).ConfigureAwait(false);
            if (r.IsOk)
            {
                lock (_pendingLock)
                {
                    _ = _pending.Remove(accountKey);
                }

                hooks.Fire(HookNames.OnTotpChange, () => new SecurityEnabledPayload(true));
            }

            return r;
        },
        ct);

    /// <inheritdoc/>
    public Task<Result<Unit>> DisableTotpAsync(string accountKey, string password, CancellationToken ct) => Task.Run(
        async () =>
        {
            if (string.IsNullOrEmpty(password))
            {
                return Result<Unit>.Fail(new ValidationError("Enter your current password."));
            }

            var patch = new JsonObject { ["currentSecret"] = password, ["otpAuth"] = new JsonObject { ["otpUrl"] = null } };
            var r = await UpdateSingletonAsync(accountKey, "x:AccountPassword", patch, "Two-step sign-in was not turned off.", ct).ConfigureAwait(false);
            if (r.IsOk)
            {
                hooks.Fire(HookNames.OnTotpChange, () => new SecurityEnabledPayload(false));
            }

            return r;
        },
        ct);

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<AccountAppPassword>>> AppPasswordsAsync(string accountKey, CancellationToken ct) => Task.Run(
        async () =>
        {
            var list = await ListAsync(accountKey, "x:AppPassword", ct).ConfigureAwait(false);
            return list.Map(items => (IReadOnlyList<AccountAppPassword>)[.. items.Select(o => new AccountAppPassword(o.Str("id") ?? string.Empty, o.Str("description") ?? string.Empty, Time(o.Str("createdAt")), Time(o.Str("expiresAt"))))]);
        },
        ct);

    /// <inheritdoc/>
    public Task<Result<AccountIssuedSecret>> CreateAppPasswordAsync(string accountKey, string name, DateTimeOffset? expires, CancellationToken ct) => Task.Run(
        async () =>
        {
            var r = await CreateCredentialAsync(accountKey, "x:AppPassword", name, expires, ct).ConfigureAwait(false);
            if (r.IsOk)
            {
                hooks.Fire(HookNames.OnAppPasswordChange, () => new AppPasswordChangePayload("created"));
            }

            return r;
        },
        ct);

    /// <inheritdoc/>
    public Task<Result<Unit>> DeleteAppPasswordAsync(string accountKey, string id, CancellationToken ct) => Task.Run(
        async () =>
        {
            var r = await DestroyAsync(accountKey, "x:AppPassword", id, ct).ConfigureAwait(false);
            if (r.IsOk)
            {
                hooks.Fire(HookNames.OnAppPasswordChange, () => new AppPasswordChangePayload("removed"));
            }

            return r;
        },
        ct);

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<AccountApiKey>>> ApiKeysAsync(string accountKey, CancellationToken ct) => Task.Run(
        async () =>
        {
            var list = await ListAsync(accountKey, "x:ApiKey", ct).ConfigureAwait(false);
            return list.Map(items => (IReadOnlyList<AccountApiKey>)[.. items.Select(o => new AccountApiKey(o.Str("id") ?? string.Empty, o.Str("description") ?? string.Empty, Time(o.Str("expiresAt"))))]);
        },
        ct);

    /// <inheritdoc/>
    public Task<Result<AccountIssuedSecret>> CreateApiKeyAsync(string accountKey, string description, DateTimeOffset? expires, CancellationToken ct) =>
        Task.Run(() => CreateCredentialAsync(accountKey, "x:ApiKey", description, expires, ct), ct);

    /// <inheritdoc/>
    public Task<Result<Unit>> DeleteApiKeyAsync(string accountKey, string id, CancellationToken ct) =>
        Task.Run(() => DestroyAsync(accountKey, "x:ApiKey", id, ct), ct);

    /// <inheritdoc/>
    public Task<Result<string?>> DisplayNameAsync(string accountKey, CancellationToken ct) => Task.Run(
        async () =>
        {
            var got = await GetSingletonAsync(accountKey, "x:AccountSettings", ct).ConfigureAwait(false);
            return got.IsOk ? Result<string?>.Ok(got.Value!.Str("description")) : Result<string?>.Fail(got.Error!);
        },
        ct);

    /// <inheritdoc/>
    public Task<Result<Unit>> SetDisplayNameAsync(string accountKey, string name, CancellationToken ct) => Task.Run(
        async () =>
        {
            ArgumentNullException.ThrowIfNull(name);
            var clean = name.Trim();
            var r = await UpdateSingletonAsync(accountKey, "x:AccountSettings", new JsonObject { ["description"] = clean }, "The name was not saved.", ct).ConfigureAwait(false);
            if (r.IsOk)
            {
                hooks.Fire(HookNames.OnDisplayNameChange, () => new DisplayNameChangePayload(clean));
            }

            return r;
        },
        ct);

    /// <inheritdoc/>
    public Task<Result<AccountSecurityEncryption>> EncryptionAsync(string accountKey, CancellationToken ct) => Task.Run(
        async () =>
        {
            var got = await GetSingletonAsync(accountKey, "x:AccountSettings", ct).ConfigureAwait(false);
            if (!got.IsOk)
            {
                return Result<AccountSecurityEncryption>.Fail(got.Error!);
            }

            var type = got.Value!["encryptionAtRest"].Str("@type") ?? "Disabled";
            var enabled = !string.Equals(type, "Disabled", StringComparison.Ordinal);
            return Result<AccountSecurityEncryption>.Ok(new AccountSecurityEncryption(enabled, enabled ? type : string.Empty));
        },
        ct);

    /// <inheritdoc/>
    public Task<Result<Unit>> SetEncryptionAsync(string accountKey, bool enabled, string? publicKey, CancellationToken ct) => Task.Run(
        async () =>
        {
            var r = enabled ? await EnableEncryptionAsync(accountKey, publicKey, ct).ConfigureAwait(false) : await UpdateSingletonAsync(accountKey, "x:AccountSettings", new JsonObject { ["encryptionAtRest"] = new JsonObject { ["@type"] = "Disabled" } }, "Encryption was not turned off.", ct).ConfigureAwait(false);
            if (r.IsOk)
            {
                hooks.Fire(HookNames.OnEncryptionChange, () => new SecurityEnabledPayload(enabled));
            }

            return r;
        },
        ct);

    private static DateTimeOffset? Time(string? text) =>
        text is not null && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null;

    private static string NewOtpUri(string user)
    {
        var secret = Base32(RandomNumberGenerator.GetBytes(20));
        var label = Uri.EscapeDataString(Issuer + ":" + user);
        return $"otpauth://totp/{label}?secret={secret}&issuer={Issuer}&algorithm=SHA1&digits=6&period=30";
    }

    private static string Base32(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var sb = new System.Text.StringBuilder();
        var buffer = 0;
        var bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            sb.Append(alphabet[(buffer << (5 - bits)) & 31]);
        }

        return sb.ToString();
    }

    private Result<IAccountSession> Managed(string accountKey)
    {
        var s = AccountCalls.Session(accounts, accountKey);
        if (!s.IsOk)
        {
            return s;
        }

        return s.Value!.Client.Session.Has(JmapCapabilities.Stalwart) ? s : Result<IAccountSession>.Fail(new ValidationError("Account security is not available on this server."));
    }

    private async Task<Result<JsonObject>> GetSingletonAsync(string accountKey, string type, CancellationToken ct)
    {
        var s = Managed(accountKey);
        if (!s.IsOk)
        {
            return Result<JsonObject>.Fail(s.Error!);
        }

        var r = await AccountCalls.CallAsync(s.Value!, type + "/get", new JsonObject { ["accountId"] = s.Value!.Client.Session.MailAccountId, ["ids"] = new JsonArray(Singleton) }, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<JsonObject>.Fail(r.Error!);
        }

        return (r.Value!["list"] as JsonArray)?.FirstOrDefault() is JsonObject item ? Result<JsonObject>.Ok(item) : Result<JsonObject>.Fail(new ValidationError("The server did not answer."));
    }

    private async Task<Result<Unit>> UpdateSingletonAsync(string accountKey, string type, JsonObject patch, string fallback, CancellationToken ct)
    {
        var s = Managed(accountKey);
        if (!s.IsOk)
        {
            return Result<Unit>.Fail(s.Error!);
        }

        var args = new JsonObject { ["accountId"] = s.Value!.Client.Session.MailAccountId, ["update"] = new JsonObject { [Singleton] = patch } };
        var r = await AccountCalls.CallAsync(s.Value, type + "/set", args, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<Unit>.Fail(r.Error!);
        }

        return AccountCalls.Failed(r.Value!, "notUpdated", Singleton) is { } bad ? Result<Unit>.Fail(AccountCalls.SetFailure(bad, fallback)) : Result<Unit>.Ok(default);
    }

    private async Task<Result<IReadOnlyList<JsonObject>>> ListAsync(string accountKey, string type, CancellationToken ct)
    {
        var s = Managed(accountKey);
        if (!s.IsOk)
        {
            return Result<IReadOnlyList<JsonObject>>.Fail(s.Error!);
        }

        var account = s.Value!.Client.Session.MailAccountId;
        var query = await AccountCalls.CallAsync(s.Value, type + "/query", new JsonObject { ["accountId"] = account }, ct).ConfigureAwait(false);
        if (!query.IsOk)
        {
            return Result<IReadOnlyList<JsonObject>>.Fail(query.Error!);
        }

        var ids = query.Value!.Strings("ids");
        if (ids.Count == 0)
        {
            return Result<IReadOnlyList<JsonObject>>.Ok([]);
        }

        var got = await AccountCalls.CallAsync(s.Value, type + "/get", new JsonObject { ["accountId"] = account, ["ids"] = new JsonArray([.. ids.Select(i => (JsonNode?)i)]) }, ct).ConfigureAwait(false);
        return got.IsOk ? Result<IReadOnlyList<JsonObject>>.Ok([.. (got.Value!["list"] as JsonArray ?? []).OfType<JsonObject>()]) : Result<IReadOnlyList<JsonObject>>.Fail(got.Error!);
    }

    private async Task<Result<AccountIssuedSecret>> CreateCredentialAsync(string accountKey, string type, string description, DateTimeOffset? expires, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return Result<AccountIssuedSecret>.Fail(new ValidationError("Give it a name."));
        }

        var s = Managed(accountKey);
        if (!s.IsOk)
        {
            return Result<AccountIssuedSecret>.Fail(s.Error!);
        }

        var body = new JsonObject { ["description"] = description.Trim() };
        if (expires is { } e)
        {
            body["expiresAt"] = e.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        var args = new JsonObject { ["accountId"] = s.Value!.Client.Session.MailAccountId, ["create"] = new JsonObject { ["new"] = body } };
        var r = await AccountCalls.CallAsync(s.Value, type + "/set", args, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<AccountIssuedSecret>.Fail(r.Error!);
        }

        if (AccountCalls.Failed(r.Value!, "notCreated", "new") is { } bad)
        {
            return Result<AccountIssuedSecret>.Fail(AccountCalls.SetFailure(bad, "It was not created."));
        }

        var made = r.Value!["created"]?["new"];
        return made.Str("id") is { Length: > 0 } id && made.Str("secret") is { Length: > 0 } secret
            ? Result<AccountIssuedSecret>.Ok(new AccountIssuedSecret(id, secret))
            : Result<AccountIssuedSecret>.Fail(new ValidationError("The server did not return the new secret."));
    }

    private async Task<Result<Unit>> DestroyAsync(string accountKey, string type, string id, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        var s = Managed(accountKey);
        if (!s.IsOk)
        {
            return Result<Unit>.Fail(s.Error!);
        }

        var args = new JsonObject { ["accountId"] = s.Value!.Client.Session.MailAccountId, ["destroy"] = new JsonArray(id) };
        var r = await AccountCalls.CallAsync(s.Value, type + "/set", args, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<Unit>.Fail(r.Error!);
        }

        return AccountCalls.Failed(r.Value!, "notDestroyed", id) is { } bad ? Result<Unit>.Fail(AccountCalls.SetFailure(bad, "It was not removed.")) : Result<Unit>.Ok(default);
    }

    private async Task<Result<Unit>> EnableEncryptionAsync(string accountKey, string? publicKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(publicKey))
        {
            return Result<Unit>.Fail(new ValidationError("Paste your public key to turn encryption on."));
        }

        var s = Managed(accountKey);
        if (!s.IsOk)
        {
            return Result<Unit>.Fail(s.Error!);
        }

        var account = s.Value!.Client.Session.MailAccountId;
        var made = await AccountCalls.CallAsync(s.Value, "x:PublicKey/set", new JsonObject { ["accountId"] = account, ["create"] = new JsonObject { ["new"] = new JsonObject { ["description"] = "Barbican", ["key"] = publicKey.Trim() } } }, ct).ConfigureAwait(false);
        if (!made.IsOk)
        {
            return Result<Unit>.Fail(made.Error!);
        }

        if (AccountCalls.Failed(made.Value!, "notCreated", "new") is { } bad)
        {
            return Result<Unit>.Fail(AccountCalls.SetFailure(bad, "The key was not accepted."));
        }

        var keyId = made.Value!["created"]?["new"].Str("id");
        if (keyId is null)
        {
            return Result<Unit>.Fail(new ValidationError("The server did not keep the key."));
        }

        // Stalwart 1.0.2 and later take the cipher from the key ("Enabled"); 1.0.0 and 1.0.1 want "Aes256": try the new shape, then the old one (Bulwark updateEncryptionAtRest)
        foreach (var shape in new[] { "Enabled", "Aes256" })
        {
            var patch = new JsonObject { ["encryptionAtRest"] = new JsonObject { ["@type"] = shape, ["publicKey"] = keyId, ["encryptOnAppend"] = false, ["allowSpamTraining"] = false } };
            var r = await UpdateSingletonAsync(accountKey, "x:AccountSettings", patch, "Encryption was not turned on.", ct).ConfigureAwait(false);
            if (r.IsOk || r.Error!.Message.Length == 0 || !r.Error.Message.Contains("@type", StringComparison.Ordinal))
            {
                return r;
            }
        }

        return Result<Unit>.Fail(new ValidationError("Encryption was not turned on."));
    }
}
