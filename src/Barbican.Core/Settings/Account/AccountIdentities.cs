// Mirrors: see IAccountIdentities; Rust jmap.rs:831 identities; Bulwark lib/jmap/client.ts:5213-5287 (Identity/set create, update, destroy), stores/identity-store.ts (createIdentityWithHooks: ask onBeforeIdentityChange, call, tell plug-ins only once the server accepted), RFC 8621 section 6.3;
// Stalwart crates/jmap/src/identity/set.rs (an email that is not one of the account's addresses is invalidProperties; mayDelete false on the default identity). The order is kept in the Inbox metadata under Barbican's own key (D-M6-A11), with ifInState, as TemplateStore does.

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;

namespace Barbican.Core.Settings.Account;

/// <summary>The default <see cref="IAccountIdentities"/>.</summary>
public sealed class AccountIdentities(IAccountManager accounts, IHookBus hooks) : IAccountIdentities
{
    private const string Metadata = "urn:ietf:params:jmap:metadata";
    private const string OrderKey = "app.barbican.identityOrder";
    private const int Tries = 3;

    private static readonly string[] Properties = ["id", "name", "email", "replyTo", "bcc", "textSignature", "htmlSignature", "mayDelete"];

    /// <inheritdoc/>
    public event EventHandler<AccountIdentityChangedEventArgs>? Changed;

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<AccountIdentity>>> ListAsync(string accountKey, CancellationToken ct) => Task.Run(() => ListCoreAsync(accountKey, ct), ct);

    /// <inheritdoc/>
    public Task<Result<AccountIdentity>> CreateAsync(string accountKey, AccountIdentityEdit edit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return Task.Run(() => CreateCoreAsync(accountKey, edit, ct), ct);
    }

    /// <inheritdoc/>
    public Task<Result<AccountIdentity>> UpdateAsync(string accountKey, string id, AccountIdentityEdit edit, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(edit);
        return Task.Run(() => UpdateCoreAsync(accountKey, id, edit, ct), ct);
    }

    /// <inheritdoc/>
    public Task<Result<Unit>> DeleteAsync(string accountKey, string id, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        return Task.Run(() => DeleteCoreAsync(accountKey, id, ct), ct);
    }

    /// <inheritdoc/>
    public Task<Result<Unit>> SetOrderAsync(string accountKey, IReadOnlyList<string> orderedIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(orderedIds);
        return Task.Run(() => OrderCoreAsync(accountKey, orderedIds, ct), ct);
    }

    private static AccountIdentity? Read(JsonNode? n)
    {
        if (n is not JsonObject o || o.Str("id") is not { Length: > 0 } id)
        {
            return null;
        }

        return new AccountIdentity(
            id,
            o.Str("name") ?? string.Empty,
            o.Str("email") ?? string.Empty,
            Join(o["replyTo"]),
            Join(o["bcc"]),
            o.Str("textSignature") ?? string.Empty,
            o.Str("htmlSignature") ?? string.Empty,
            o["mayDelete"] is not JsonValue v || !v.TryGetValue<bool>(out var may) || may);
    }

    private static string? Join(JsonNode? addresses)
    {
        if (addresses is not JsonArray a || a.Count == 0)
        {
            return null;
        }

        return string.Join(", ", a.OfType<JsonObject>().Select(x => x.Str("email") is { } e ? (x.Str("name") is { Length: > 0 } n ? $"{n} <{e}>" : e) : null).OfType<string>());
    }

    private static JsonArray? Addresses(string? text)
    {
        var list = AccountIdentityRules.ParseList(text);
        return list.Count == 0 ? null : new JsonArray([.. list.Select(a => (JsonNode?)new JsonObject { ["name"] = a.Name, ["email"] = a.Email })]);
    }

    private static IdentityViewPayload View(AccountIdentity i) => new(i.Id, i.Name, i.Email, i.ReplyTo, i.Bcc, i.TextSignature, i.HtmlSignature);

    private static JsonObject Body(AccountIdentityEdit edit, bool create)
    {
        var o = new JsonObject();
        if (edit.Name is not null)
        {
            o["name"] = edit.Name.Trim();
        }

        // the address of an identity cannot change after it was created (RFC 8621 section 6.1: immutable)
        if (create && edit.Email is not null)
        {
            o["email"] = edit.Email.Trim();
        }

        if (edit.ReplyTo is not null)
        {
            o["replyTo"] = Addresses(edit.ReplyTo);
        }

        if (edit.Bcc is not null)
        {
            o["bcc"] = Addresses(edit.Bcc);
        }

        if (edit.TextSignature is not null)
        {
            o["textSignature"] = edit.TextSignature;
        }

        if (edit.HtmlSignature is not null)
        {
            o["htmlSignature"] = edit.HtmlSignature;
        }

        return o;
    }

    private static Result<ComposeContext> Context(IAccountManager accounts, string accountKey)
    {
        var ctx = ComposeContext.For(accounts, accountKey);
        if (!ctx.IsOk)
        {
            return ctx;
        }

        return ctx.Value!.Client.Session.Has(Barbican.Core.Jmap.JmapCapabilities.Submission)
            ? ctx
            : Result<ComposeContext>.Fail(new ValidationError("This server has no sender identities."));
    }

    private async Task<Result<IReadOnlyList<AccountIdentity>>> ListCoreAsync(string accountKey, CancellationToken ct)
    {
        var ctx = ComposeContext.For(accounts, accountKey);
        if (!ctx.IsOk)
        {
            return Result<IReadOnlyList<AccountIdentity>>.Fail(ctx.Error!);
        }

        if (!ctx.Value!.Client.Session.Has(Barbican.Core.Jmap.JmapCapabilities.Submission))
        {
            return Result<IReadOnlyList<AccountIdentity>>.Ok([]);
        }

        var all = await GetAsync(ctx.Value, null, ct).ConfigureAwait(false);
        if (!all.IsOk)
        {
            return Result<IReadOnlyList<AccountIdentity>>.Fail(all.Error!);
        }

        var order = await ReadOrderAsync(ctx.Value, ct).ConfigureAwait(false);
        var ordered = AccountIdentityRules.Ordered(all.Value!, order.IsOk ? order.Value!.Ids : []);
        hooks.Fire(HookNames.OnIdentitiesLoaded, () => ordered.Select(View).ToList());
        return Result<IReadOnlyList<AccountIdentity>>.Ok(ordered);
    }

    private static async Task<Result<IReadOnlyList<AccountIdentity>>> GetAsync(ComposeContext ctx, IReadOnlyList<string>? ids, CancellationToken ct)
    {
        var args = new JsonObject
        {
            ["accountId"] = ctx.SubmissionAccount,
            ["ids"] = ids is null ? null : new JsonArray([.. ids.Select(i => (JsonNode?)i)]),
            ["properties"] = new JsonArray([.. Properties.Select(p => (JsonNode?)p)]),
        };
        var r = await AccountCalls.CallAsync(ctx.Session, "Identity/get", args, ct).ConfigureAwait(false);
        return r.IsOk
            ? Result<IReadOnlyList<AccountIdentity>>.Ok([.. (r.Value!["list"] as JsonArray ?? []).Select(Read).OfType<AccountIdentity>()])
            : Result<IReadOnlyList<AccountIdentity>>.Fail(r.Error!);
    }

    private async Task<Result<AccountIdentity>> CreateCoreAsync(string accountKey, AccountIdentityEdit edit, CancellationToken ct)
    {
        if (AccountIdentityRules.Problem(edit, isCreate: true) is { } problem)
        {
            return Result<AccountIdentity>.Fail(new ValidationError(problem));
        }

        var ctx = Context(accounts, accountKey);
        if (!ctx.IsOk)
        {
            return Result<AccountIdentity>.Fail(ctx.Error!);
        }

        var draft = new IdentityViewPayload(string.Empty, edit.Name ?? string.Empty, edit.Email ?? string.Empty, edit.ReplyTo, edit.Bcc, edit.TextSignature ?? string.Empty, edit.HtmlSignature ?? string.Empty);
        if (!await hooks.AllowAsync(HookNames.OnBeforeIdentityChange, () => new IdentityChangePayload(null, "create", draft), ct).ConfigureAwait(false))
        {
            return Result<AccountIdentity>.Fail(new ValidationError("A plug-in stopped this change."));
        }

        var args = new JsonObject { ["accountId"] = ctx.Value!.SubmissionAccount, ["create"] = new JsonObject { ["new"] = Body(edit, create: true) } };
        var r = await AccountCalls.CallAsync(ctx.Value.Session, "Identity/set", args, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<AccountIdentity>.Fail(r.Error!);
        }

        if (AccountCalls.Failed(r.Value!, "notCreated", "new") is { } bad)
        {
            return Result<AccountIdentity>.Fail(AccountCalls.SetFailure(bad, "The server did not create the identity."));
        }

        var id = r.Value!["created"]?["new"].Str("id");
        if (id is null)
        {
            return Result<AccountIdentity>.Fail(new ValidationError("The server did not create the identity."));
        }

        var got = await GetAsync(ctx.Value, [id], ct).ConfigureAwait(false);
        var created = got.IsOk && got.Value!.Count > 0 ? got.Value[0] : null;
        created ??= new AccountIdentity(id, edit.Name ?? string.Empty, edit.Email ?? string.Empty, edit.ReplyTo, edit.Bcc, edit.TextSignature ?? string.Empty, edit.HtmlSignature ?? string.Empty, true);
        hooks.Fire(HookNames.OnIdentityCreate, () => View(created));
        Changed?.Invoke(this, new AccountIdentityChangedEventArgs(accountKey));
        return Result<AccountIdentity>.Ok(created);
    }

    private async Task<Result<AccountIdentity>> UpdateCoreAsync(string accountKey, string id, AccountIdentityEdit edit, CancellationToken ct)
    {
        // the address cannot change, so it is not checked on an update
        if (AccountIdentityRules.Problem(edit with { Email = null }, isCreate: false) is { } problem)
        {
            return Result<AccountIdentity>.Fail(new ValidationError(problem));
        }

        var ctx = Context(accounts, accountKey);
        if (!ctx.IsOk)
        {
            return Result<AccountIdentity>.Fail(ctx.Error!);
        }

        var next = new IdentityViewPayload(id, edit.Name ?? string.Empty, edit.Email ?? string.Empty, edit.ReplyTo, edit.Bcc, edit.TextSignature ?? string.Empty, edit.HtmlSignature ?? string.Empty);
        if (!await hooks.AllowAsync(HookNames.OnBeforeIdentityChange, () => new IdentityChangePayload(id, "update", next), ct).ConfigureAwait(false))
        {
            return Result<AccountIdentity>.Fail(new ValidationError("A plug-in stopped this change."));
        }

        var args = new JsonObject { ["accountId"] = ctx.Value!.SubmissionAccount, ["update"] = new JsonObject { [id] = Body(edit, create: false) } };
        var r = await AccountCalls.CallAsync(ctx.Value.Session, "Identity/set", args, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<AccountIdentity>.Fail(r.Error!);
        }

        if (AccountCalls.Failed(r.Value!, "notUpdated", id) is { } bad)
        {
            return Result<AccountIdentity>.Fail(AccountCalls.SetFailure(bad, "The server did not change the identity."));
        }

        var got = await GetAsync(ctx.Value, [id], ct).ConfigureAwait(false);
        if (!got.IsOk || got.Value!.Count == 0)
        {
            return Result<AccountIdentity>.Fail(got.Error ?? new ValidationError("The identity is gone."));
        }

        var updated = got.Value[0];
        hooks.Fire(HookNames.OnIdentityUpdate, () => View(updated));
        Changed?.Invoke(this, new AccountIdentityChangedEventArgs(accountKey));
        return Result<AccountIdentity>.Ok(updated);
    }

    private async Task<Result<Unit>> DeleteCoreAsync(string accountKey, string id, CancellationToken ct)
    {
        var ctx = Context(accounts, accountKey);
        if (!ctx.IsOk)
        {
            return Result<Unit>.Fail(ctx.Error!);
        }

        var known = await GetAsync(ctx.Value!, [id], ct).ConfigureAwait(false);
        if (known.IsOk && known.Value!.Count > 0 && !known.Value[0].MayDelete)
        {
            return Result<Unit>.Fail(new ValidationError("The default identity of the account cannot be deleted."));
        }

        if (!await hooks.AllowAsync(HookNames.OnBeforeIdentityChange, () => new IdentityChangePayload(id, "delete", null), ct).ConfigureAwait(false))
        {
            return Result<Unit>.Fail(new ValidationError("A plug-in stopped this change."));
        }

        var args = new JsonObject { ["accountId"] = ctx.Value!.SubmissionAccount, ["destroy"] = new JsonArray(id) };
        var r = await AccountCalls.CallAsync(ctx.Value.Session, "Identity/set", args, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<Unit>.Fail(r.Error!);
        }

        if (AccountCalls.Failed(r.Value!, "notDestroyed", id) is { } bad)
        {
            return Result<Unit>.Fail(AccountCalls.SetFailure(bad, "The server did not delete the identity."));
        }

        hooks.Fire(HookNames.OnIdentityDelete, () => new IdentityDeletePayload(id));
        Changed?.Invoke(this, new AccountIdentityChangedEventArgs(accountKey));
        return Result<Unit>.Ok(default);
    }

    private static async Task<Result<(string Inbox, string? State, IReadOnlyList<string> Ids)>> ReadOrderAsync(ComposeContext ctx, CancellationToken ct)
    {
        if (!ctx.Client.Session.Has(Metadata))
        {
            return Result<(string, string?, IReadOnlyList<string>)>.Ok((string.Empty, null, []));
        }

        var roles = await ctx.RolesAsync(false, ct).ConfigureAwait(false);
        if (!roles.IsOk || roles.Value!.Inbox is not { } inbox)
        {
            return Result<(string, string?, IReadOnlyList<string>)>.Fail(roles.Error ?? new ValidationError("No Inbox."));
        }

        var r = await AccountCalls.CallAsync(ctx.Session, "Mailbox/get", new JsonObject { ["accountId"] = ctx.Account, ["ids"] = new JsonArray(inbox), ["properties"] = new JsonArray("metadata") }, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<(string, string?, IReadOnlyList<string>)>.Fail(r.Error!);
        }

        var meta = ((r.Value!["list"] as JsonArray)?.FirstOrDefault() as JsonObject)?["metadata"] as JsonObject;
        IReadOnlyList<string> ids = meta?[OrderKey] is JsonObject entry ? entry.Strings("ids") : [];
        return Result<(string, string?, IReadOnlyList<string>)>.Ok((inbox, r.Value.Str("state"), ids));
    }

    private async Task<Result<Unit>> OrderCoreAsync(string accountKey, IReadOnlyList<string> orderedIds, CancellationToken ct)
    {
        var ctx = Context(accounts, accountKey);
        if (!ctx.IsOk)
        {
            return Result<Unit>.Fail(ctx.Error!);
        }

        if (!ctx.Value!.Client.Session.Has(Metadata))
        {
            return Result<Unit>.Fail(new ValidationError("This server cannot keep the order of the identities."));
        }

        for (var attempt = 0; attempt < Tries; attempt++)
        {
            var read = await ReadOrderAsync(ctx.Value, ct).ConfigureAwait(false);
            if (!read.IsOk)
            {
                return Result<Unit>.Fail(read.Error!);
            }

            var (inbox, state, _) = read.Value;
            var patch = new JsonObject { ["metadata/" + OrderKey] = new JsonObject { ["ids"] = new JsonArray([.. orderedIds.Select(i => (JsonNode?)i)]) } };
            var args = new JsonObject { ["accountId"] = ctx.Value.Account, ["update"] = new JsonObject { [inbox] = patch } };
            if (state is not null)
            {
                args["ifInState"] = state;
            }

            var w = await AccountCalls.CallAsync(ctx.Value.Session, "Mailbox/set", args, ct).ConfigureAwait(false);
            if (w.IsOk)
            {
                if (AccountCalls.Failed(w.Value!, "notUpdated", inbox) is { } bad)
                {
                    return Result<Unit>.Fail(AccountCalls.SetFailure(bad, "The order was not saved."));
                }

                Changed?.Invoke(this, new AccountIdentityChangedEventArgs(accountKey));
                return Result<Unit>.Ok(default);
            }

            if (w.Error is not MethodError { Type: "stateMismatch" })
            {
                return Result<Unit>.Fail(w.Error!);
            }
        }

        return Result<Unit>.Fail(new ValidationError("The identities kept changing on another device; try again."));
    }
}
