// Bulwark lib/admin/policy-scopes.ts and lib/org/signature-state.ts.
using Barbican.Core.Compose.Recipients;

namespace Barbican.Core.OrganizationPolicy;

public static class OrganizationPolicyResolver
{
    public static EffectiveOrganizationPolicy Resolve(OrganizationPolicyDocument document, OrganizationPolicySubject subject)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(subject);
        if (subject.Groups is null) throw new ArgumentException("A complete account subject is required.", nameof(subject));
        var email = NormalizeAddress(subject.Email) ?? throw new ArgumentException("A complete account subject is required.", nameof(subject));
        var domain = email[(email.LastIndexOf('@') + 1)..];
        var groups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in subject.Groups)
            groups.Add(NormalizeAddress(group) ?? throw new ArgumentException("A complete account subject is required.", nameof(subject)));
        var domainScopes = document.Scopes.Where(scope => scope.Domains.Any(pattern => DomainMatches(pattern, domain))).ToArray();
        var matched = domainScopes.Concat(document.Scopes.Where(scope => !domainScopes.Contains(scope) && scope.Groups.Any(groups.Contains))).ToArray();
        var noPrint = document.Global.NoPrint ?? false;
        var watermark = document.Global.Watermark ?? false;
        var labels = document.Global.Labels ?? new ClassificationPolicy([], "", false, "");
        var noPrintSource = document.Global.NoPrint is null ? "default" : "global";
        var watermarkSource = document.Global.Watermark is null ? "default" : "global";
        var labelsSource = document.Global.Labels is null ? "default" : "global";
        var proofreading = document.Global.Proofreading;
        var reportAddress = document.Global.PhishingReportAddress ?? string.Empty;
        var reportFormat = document.Global.PhishingReportFormat ?? PhishingReportFormat.Forward;
        foreach (var scope in matched)
        {
            if (scope.Values.NoPrint is { } p) { noPrint = p; noPrintSource = scope.Id; }
            if (scope.Values.Watermark is { } w) { watermark = w; watermarkSource = scope.Id; }
            if (scope.Values.Labels is { } l) { labels = l; labelsSource = scope.Id; }
            proofreading = proofreading.Apply(scope.Values.Proofreading);
            // a scope replaces the phishing report per leaf (Bulwark organisation-policy.ts: address and format each inherit)
            if (scope.Values.PhishingReportAddress is { } address) reportAddress = address;
            if (scope.Values.PhishingReportFormat is { } format) reportFormat = format;
        }
        return new(noPrint, watermark, labels, [.. matched.Select(scope => scope.Id)], noPrintSource, watermarkSource, labelsSource)
        {
            Proofreading = proofreading,
            PhishingReport = reportAddress.Length == 0 ? PhishingReportPolicy.Off : new PhishingReportPolicy(reportAddress, reportFormat),
        };
    }

    public static EffectiveOrganizationPolicy ResolveForSender(OrganizationPolicyDocument document, OrganizationPolicySubject subject, string? senderEmail)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(subject);
        var accountPolicy = Resolve(document, subject);
        var address = senderEmail is null ? null : NormalizeAddress(senderEmail);
        if (senderEmail is not null && address is null) throw new ArgumentException("A valid sender is required.", nameof(senderEmail));
        if (!string.IsNullOrEmpty(address) && subject.Groups.Any(group => string.Equals(NormalizeAddress(group), address, StringComparison.Ordinal)) &&
            document.Scopes.Any(scope => scope.Groups.Contains(address, StringComparer.Ordinal)))
            return Resolve(document, subject with { Groups = [address] });
        return accountPolicy;
    }

    private static string? NormalizeAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var address = value.Trim();
        return RecipientText.IsValidEmail(address) ? RecipientText.ToAsciiEmail(address).ToLowerInvariant() : null;
    }

    private static bool DomainMatches(string pattern, string domain) => pattern.StartsWith("*.", StringComparison.Ordinal)
        ? domain.EndsWith(pattern[1..], StringComparison.Ordinal) && domain.Length > pattern.Length - 1
        : string.Equals(pattern, domain, StringComparison.Ordinal);
}
