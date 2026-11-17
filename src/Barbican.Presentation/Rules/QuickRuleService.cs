// Mirrors: Rust quick_rules.rs:229 save and reader.rs rx-* (the rule items of the More menu): the message's senders and List-Id, the folder's Sieve path, then QuickRuleSaver;
// Bulwark lib/filters/quick-rule-flow.ts:345. The toast says what happened ("Rule ... created", "Added to rule ...", "Already covered by ..."); a hand-edited script is refused.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Filters.QuickRules;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Jmap.Requests;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Service;
using Barbican.Core.Sieve;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Mail;

namespace Barbican.Presentation.Rules;

/// <summary>Makes a quick rule from a message and saves it into the account's Sieve script.</summary>
public sealed class QuickRuleService(IActiveMail mail, IMailboxDirectory folders, IToastService? toasts)
{
    /// <summary>What "Manage rules" says until the filter editor arrives in M6.</summary>
    public const string ManageMessage = "Filters arrive in M6";

    private IJmapClient? Client => (mail.Mail as MailService)?.Client;

    /// <summary>The subject of a message: its senders (the user's own left out), their domain and its List-Id; null when the message cannot be read.</summary>
    public async Task<Result<QuickRuleSubject>> SubjectAsync(MailTarget target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (Client is not { } client)
        {
            return Result<QuickRuleSubject>.Fail(new ValidationError("No account is open."));
        }

        // a rule is made on the account on screen: a unified row of another login is that login's to filter (Fable review 3 R3-02)
        if (!string.Equals(target.Login, mail.AccountKey ?? target.AccountKey, StringComparison.Ordinal))
        {
            return Result<QuickRuleSubject>.Fail(new ValidationError("Switch to that message's account to make a rule for it."));
        }

        var args = new JsonObject
        {
            ["accountId"] = target.JmapAccountId(client.Session.MailAccountId),
            ["ids"] = new JsonArray(target.ServerId),
            ["properties"] = new JsonArray("from", "header:List-Id:asText"),
        };
        var r = await client.CallAsync(JmapRequest.Of(JmapRequest.Call("Email/get", args, "g")), ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<QuickRuleSubject>.Fail(r.Error!);
        }

        if (r.Value!.MethodResponses[0][1].Arr("list")?.FirstOrDefault() is not JsonObject email)
        {
            return Result<QuickRuleSubject>.Fail(new ValidationError("That message is gone."));
        }

        var message = new JsonObject { ["from"] = email["from"]?.DeepClone() };
        if (email.Str("header:List-Id:asText") is { Length: > 0 } listId)
        {
            message["headers"] = new JsonArray(new JsonObject { ["name"] = "List-Id", ["value"] = listId });
        }

        var own = new List<string>();
        if (mail.Mail is { } m && await m.GetIdentitiesAsync(ct).ConfigureAwait(false) is { IsOk: true } ids)
        {
            own.AddRange(ids.Value!.Select(i => i.Email));
        }

        if (mail.AccountUser is { } user)
        {
            own.Add(user);
        }

        var subject = QuickRuleBuilder.SubjectOf(message, own);
        return subject.Senders.Count == 0
            ? Result<QuickRuleSubject>.Fail(new ValidationError("This message has no sender to make a rule from."))
            : Result<QuickRuleSubject>.Ok(subject);
    }

    /// <summary>The folder as a rule target (id, Sieve path, name); null when the account has no such folder.</summary>
    public QuickRuleFolder? FolderOf(string id)
    {
        var all = folders.Tree.All;
        var f = all.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));
        if (f is null)
        {
            return null;
        }

        var refs = all.Select(x => new SieveFolderRef(x.Id, x.Name, x.ParentId, x.Role.ToString().ToLowerInvariant())).ToList();
        return new QuickRuleFolder(f.Id, QuickRuleBuilder.SievePath(refs, f.Id), f.Name);
    }

    /// <summary>The Junk folder, or null when the account has none ("No Junk folder").</summary>
    public QuickRuleFolder? JunkFolder() =>
        folders.Tree.All.FirstOrDefault(m => m.Role == MailboxRole.Junk) is { } j ? FolderOf(j.Id) : null;

    /// <summary>Saves the preset made from the message; the result is the toast text.</summary>
    public async Task<Result<string>> ApplyAsync(MailTarget target, Func<QuickRuleSubject, QuickRulePreset?> preset, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(preset);
        if (Client is not { } client)
        {
            return Result<string>.Fail(new ValidationError("No account is open."));
        }

        var subject = await SubjectAsync(target, ct).ConfigureAwait(false);
        if (!subject.IsOk)
        {
            return Result<string>.Fail(subject.Error!);
        }

        if (preset(subject.Value!) is not { } p)
        {
            return Result<string>.Fail(new ValidationError("That folder is not available."));
        }

        var candidate = QuickRuleBuilder.PresetRule(p, subject.Value!, QuickRuleBuilder.NewId());
        var saved = await new QuickRuleSaver(new SieveScriptStore(client)).SaveAsync(candidate, ct).ConfigureAwait(false);
        if (saved.IsOk)
        {
            toasts?.Show(new ToastRequest(saved.Value!));
        }

        return saved;
    }

    /// <summary>"Manage rules": the filter editor is M6.</summary>
    public void Manage() => toasts?.Show(new ToastRequest(ManageMessage));
}
