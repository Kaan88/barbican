// Mirrors Bulwark lib/outgoing/classification.ts and lib/classification/labels.ts.
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Compose.Recipients;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Submission;
using Barbican.Core.Mail.Model;
using Barbican.Core.OrganizationPolicy;

namespace Barbican.Core.Compose;

/// <summary>Resolves classification under the exact sending login and verified identity.</summary>
public sealed class ComposerClassification(OrganizationPolicyService policy)
{
    public async Task<ClassificationPolicy?> PreviewAsync(IAccountSession exactOwner, string? identityId, string sender,
        string? subAddressDelimiter, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(exactOwner);
        var lookup = await policy.ReadForSessionAsync(new AccountSessionSnapshot(exactOwner, null), ct).ConfigureAwait(false);
        if (lookup.Snapshot is not { } snapshot || snapshot.ForSender(sender) is not { } effective) return null;
        var session = exactOwner.Client.Session;
        var ids = await IdentityService.ListCapturedAsync(exactOwner.Client,
            session.AccountFor(JmapCapabilities.Submission) ?? session.MailAccountId, ct).ConfigureAwait(false);
        if (ids.Value is not { } found ||
            SubmissionRules.ChooseIdentity([.. found.Select(item => item.Identity)], identityId ?? "", sender) is not { } identity ||
            !CoversSender(identity, sender, subAddressDelimiter)) return null;
        return snapshot.TryRunWhileCurrent(static () => true) ? effective.Labels : null;
    }

    internal static bool CoversSender(Identity identity, string sender, string? subAddressDelimiter)
    {
        if (!RecipientText.IsValidEmail(sender)) return false;
        if (string.Equals(identity.Email, sender, StringComparison.OrdinalIgnoreCase) ||
            SubmissionRules.WildcardCovers(identity.Email, sender)) return true;
        if (subAddressDelimiter is not { Length: > 0 and <= 8 }) return false;
        var at = identity.Email.LastIndexOf('@');
        if (at <= 0) return false;
        var prefix = identity.Email[..at] + subAddressDelimiter;
        var suffix = identity.Email[at..];
        var tagLength = sender.Length - prefix.Length - suffix.Length;
        return tagLength is > 0 and <= 30 && sender.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            sender.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
            sender.Substring(prefix.Length, tagLength).All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
    }

    public async Task<Result<ComposerClassificationDecision>> ResolveAsync(IAccountSession exactOwner, string verifiedSender,
        ComposerClassificationChoice? choice, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(exactOwner);
        ArgumentException.ThrowIfNullOrWhiteSpace(verifiedSender);
        var captured = new AccountSessionSnapshot(exactOwner, null);
        var lookup = await policy.ReadForSessionAsync(captured, ct).ConfigureAwait(false);
        if (lookup.Status == OrganizationPolicyTransportStatus.Missing && choice is null)
            return Result<ComposerClassificationDecision>.Ok(new(policy, captured, verifiedSender, null, null, null));
        if (lookup.Status != OrganizationPolicyTransportStatus.Authoritative || lookup.Snapshot is not { } snapshot ||
            snapshot.ForSender(verifiedSender) is not { } effective)
            return Denied();
        var labels = effective.Labels;
        var selected = choice is null
            ? labels.List.FirstOrDefault(label => string.Equals(label.Id, labels.DefaultId, StringComparison.Ordinal))
            : labels.List.FirstOrDefault(label => string.Equals(label.Id, choice.LabelId, StringComparison.Ordinal));
        if ((choice?.LabelId is not null && selected is null) || (labels.Required && selected is null)) return Denied();
        return Result<ComposerClassificationDecision>.Ok(new(policy, captured, verifiedSender, labels, selected, snapshot));
    }

    private static Result<ComposerClassificationDecision> Denied() =>
        Result<ComposerClassificationDecision>.Fail(new ValidationError("Classification policy or choice could not be verified."));
}
