// Mirrors Bulwark lib/outgoing/classification.ts and lib/classification/labels.ts.
using Barbican.Core.Accounts;
using Barbican.Core.OrganizationPolicy;

namespace Barbican.Core.Compose;

/// <summary>A classification decision tied to the provider and sending session that authorized it.</summary>
public sealed class ComposerClassificationDecision
{
    private readonly OrganizationPolicyService _service;
    private readonly AccountSessionSnapshot _owner;
    private readonly string _sender;
    private readonly OrganizationPolicySnapshot? _snapshot;

    internal ComposerClassificationDecision(OrganizationPolicyService service, AccountSessionSnapshot owner, string sender,
        ClassificationPolicy? policy, ClassificationLabel? label, OrganizationPolicySnapshot? snapshot)
    {
        _service = service;
        _owner = owner;
        _sender = sender;
        _snapshot = snapshot;
        Policy = policy is null ? null : policy with { List = policy.List.ToArray() };
        Label = label;
        Headers = ComposerClassificationHeaders.Create(label, policy?.ExtraHeader ?? "");
    }

    public ClassificationPolicy? Policy { get; }
    public ClassificationLabel? Label { get; }
    public IReadOnlyDictionary<string, string> Headers { get; }
    public bool Matches(IAccountSession session, string verifiedSender) => ReferenceEquals(_owner.Session, session) &&
        string.Equals(_sender, verifiedSender, StringComparison.OrdinalIgnoreCase);
    public string ApplySubjectPrefix(string subject) => Policy is null || Policy.List.Count == 0 ? subject :
        ComposerClassificationHeaders.ApplySubjectPrefix(subject, Label, Policy.List);

    public bool TryIssue(Func<bool> issue) => _snapshot is null
        ? _service.TryRunWithoutProvider(_owner, issue) : _snapshot.TryRunWhileCurrent(issue);

    public async Task<bool> IsStillApplicableAsync(CancellationToken ct)
    {
        var lookup = await _service.ReadForSessionAsync(_owner, ct).ConfigureAwait(false);
        if (Policy is null) return lookup.Status == OrganizationPolicyTransportStatus.Missing && TryIssue(static () => true);
        var current = lookup.Status == OrganizationPolicyTransportStatus.Authoritative ? lookup.Snapshot?.ForSender(_sender)?.Labels : null;
        return current is not null && Same(Policy, current) && lookup.Snapshot!.TryRunWhileCurrent(static () => true) && TryIssue(static () => true);
    }

    private static bool Same(ClassificationPolicy first, ClassificationPolicy second) => first.Required == second.Required &&
        string.Equals(first.DefaultId, second.DefaultId, StringComparison.Ordinal) &&
        string.Equals(first.ExtraHeader, second.ExtraHeader, StringComparison.Ordinal) && first.List.SequenceEqual(second.List);
}
