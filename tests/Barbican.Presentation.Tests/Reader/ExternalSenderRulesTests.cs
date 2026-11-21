// Ported from Bulwark components/email/__tests__/external-sender-chip.test.tsx and external-sender-chip-detail.test.tsx,
// lib/external-mail.ts:29-132 (addressDomain, domainMatches, internalDomains, externalSenders) and external-sender-chip.tsx:120-125 (tooltip).
// G-051: the reader's External chip.

using Barbican.Core.Mail.Model;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Reader;

namespace Barbican.Presentation.Tests.Reader;

public sealed class ExternalSenderRulesTests
{
    private static readonly IReadOnlyList<string> Org = ExternalSenderRules.InternalDomains(["org-alice@example.org"]);

    [Fact]
    public void A_sender_in_an_identity_domain_has_no_chip()
    {
        Assert.Empty(ExternalSenderRules.ExternalSenderDomains(["org-bob@example.org"], Org));
        Assert.Empty(ExternalSenderRules.ExternalSenderDomains(["Org-Bob@EXAMPLE.ORG"], Org));
    }

    [Fact]
    public void A_subdomain_of_an_identity_domain_counts_as_internal()
    {
        Assert.Empty(ExternalSenderRules.ExternalSenderDomains(["someone@eu.example.org"], Org));
        // the other way round a listed subdomain does not cover its parent
        var eu = ExternalSenderRules.InternalDomains(["alice@eu.example.org"]);
        Assert.Single(ExternalSenderRules.ExternalSenderDomains(["bob@example.org"], eu));
    }

    [Fact]
    public void A_domain_outside_the_identities_is_external_and_named_in_the_tooltip()
    {
        var domains = ExternalSenderRules.ExternalSenderDomains(["dave@org-two.test"], Org);
        Assert.Equal("org-two.test", Assert.Single(domains));
        Assert.Equal("From org-two.test, outside your organisation", ExternalSenderRules.Tooltip(domains));
    }

    [Fact]
    public void Several_external_senders_name_each_domain_once()
    {
        var domains = ExternalSenderRules.ExternalSenderDomains(["bob@evil.example", "eve@evil.example", "dave@org-two.test"], Org);
        Assert.Equal("From evil.example, org-two.test, outside your organisation", ExternalSenderRules.Tooltip(domains));
    }

    [Fact]
    public void An_address_without_a_domain_counts_and_says_so()
    {
        var domains = ExternalSenderRules.ExternalSenderDomains(["ceo"], Org);
        Assert.Equal(string.Empty, Assert.Single(domains));
        Assert.Equal("From an address without a domain, outside your organisation", ExternalSenderRules.Tooltip(domains));
    }

    [Fact]
    public void With_no_known_identity_domain_nobody_can_be_told_apart()
    {
        IReadOnlyList<string> none = ExternalSenderRules.InternalDomains([]);
        Assert.Empty(none);
        Assert.Empty(ExternalSenderRules.ExternalSenderDomains(["dave@org-two.test"], none));
        // a domain without a dot is not a usable organisation domain (Bulwark DOMAIN_RE)
        Assert.Empty(ExternalSenderRules.InternalDomains(["alice@localhost"]));
    }

    [Fact]
    public void Domains_compare_in_ascii_form_so_an_idn_matches_its_punycode()
    {
        var idn = ExternalSenderRules.InternalDomains(["alice@bücher.example"]);
        Assert.Equal("xn--bcher-kva.example", Assert.Single(idn));
        Assert.Empty(ExternalSenderRules.ExternalSenderDomains(["bob@xn--bcher-kva.example"], idn));
    }

    [Fact]
    public void A_display_name_wrapper_and_a_trailing_dot_do_not_change_the_domain()
    {
        Assert.Equal("org-two.test", ExternalSenderRules.AddressDomain("Dave <dave@org-two.test>"));
        Assert.Equal("org-two.test", ExternalSenderRules.AddressDomain("dave@org-two.test."));
    }

    [Fact]
    public void The_reader_header_shows_the_chip_for_an_external_sender()
    {
        var snapshot = ReaderSnapshots.FromBody(Body("dave@org-two.test"), null, Options(label: true));

        Assert.Equal("From org-two.test, outside your organisation", snapshot.Header.ExternalSenderTooltip);
    }

    [Fact]
    public void The_reader_header_keeps_the_chip_away_when_the_setting_is_off_or_the_domain_is_ours()
    {
        Assert.Empty(ReaderSnapshots.FromBody(Body("dave@org-two.test"), null, Options(label: false)).Header.ExternalSenderTooltip);
        Assert.Empty(ReaderSnapshots.FromBody(Body("org-bob@example.org"), null, Options(label: true)).Header.ExternalSenderTooltip);
    }

    private static ReaderOptions Options(bool label) => new()
    {
        ExternalSenderLabel = label,
        InternalDomains = Org,
    };

    private static EmailBody Body(string from) => new()
    {
        Id = "m1",
        Subject = "Hello",
        From = [new EmailAddress("Sender", from)],
        To = [new EmailAddress("Org Alice", "org-alice@example.org")],
        ReceivedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
    };
}
