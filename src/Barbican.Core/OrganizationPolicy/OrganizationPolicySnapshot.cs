using Barbican.Core.Compose.Recipients;

namespace Barbican.Core.OrganizationPolicy;

/// <summary>Effective policy from a trusted response under one captured provider and account session.</summary>
public sealed class OrganizationPolicySnapshot
{
    private readonly OrganizationPolicyDocument _document;
    private readonly OrganizationPolicySubject _subject;
    private readonly IReadOnlyList<string> _domains;
    private readonly Func<Func<bool>, bool> _whileCurrent;

    internal OrganizationPolicySnapshot(string accountKey, OrganizationPolicyDocument document, OrganizationPolicySubject subject,
        IReadOnlyList<string> domains, Func<Func<bool>, bool> whileCurrent)
    {
        LocalAccountKey = accountKey;
        _document = document;
        _subject = subject;
        _domains = domains;
        _whileCurrent = whileCurrent;
        Effective = OrganizationPolicyResolver.Resolve(document, subject);
    }

    public string LocalAccountKey { get; }

    public EffectiveOrganizationPolicy Effective { get; }

    /// <summary>Resolves a caller-verified sender only if the server response covers its address domain.</summary>
    public EffectiveOrganizationPolicy? ForSender(string verifiedAddress) => Covers(_domains, verifiedAddress)
        ? OrganizationPolicyResolver.ResolveForSender(_document, _subject, verifiedAddress) : null;

    /// <summary>Issues a synchronous action while the provider and captured login still match.</summary>
    public bool TryRunWhileCurrent(Func<bool> issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        return _whileCurrent(issue);
    }

    internal static bool Covers(IReadOnlyList<string> domains, string address)
    {
        if (!RecipientText.IsValidEmail(address)) return false;
        var normalized = RecipientText.ToAsciiEmail(address);
        var domain = normalized[(normalized.LastIndexOf('@') + 1)..];
        return domains.Contains(domain, StringComparer.OrdinalIgnoreCase);
    }
}
