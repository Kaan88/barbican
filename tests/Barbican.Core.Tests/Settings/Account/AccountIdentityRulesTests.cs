// Not Rust rows: G-023..G-025, I-291 (Bulwark identity-settings.tsx validation, identity-store.ts order by preferredIdentityIds). M6 stream set-data.

using Barbican.Core.Settings.Account;

namespace Barbican.Core.Tests.Settings.Account;

public sealed class AccountIdentityRulesTests
{
    private static AccountIdentity I(string id) => new(id, id, id + "@x.example", null, null, string.Empty, string.Empty, true);

    [Fact]
    [Red("set-data")]
    public void Addresses_are_checked_before_an_identity_is_sent()
    {
        Assert.Null(AccountIdentityRules.Problem(new AccountIdentityEdit("Ann", "ann@x.example", null, null, null, null), isCreate: true));
        Assert.Null(AccountIdentityRules.Problem(new AccountIdentityEdit(null, "*@x.example", "a@b.example, c@d.example", "e@f.example", null, null), isCreate: true));
        Assert.NotNull(AccountIdentityRules.Problem(new AccountIdentityEdit("Ann", "ann", null, null, null, null), isCreate: true));
        Assert.NotNull(AccountIdentityRules.Problem(new AccountIdentityEdit("Ann", null, null, null, null, null), isCreate: true));
        Assert.NotNull(AccountIdentityRules.Problem(new AccountIdentityEdit(null, "ann@x.example", "nope", null, null, null), isCreate: false));
        Assert.Null(AccountIdentityRules.Problem(new AccountIdentityEdit("New name", null, null, null, null, null), isCreate: false));
    }

    [Fact]
    [Red("set-data")]
    public void Identities_follow_the_stored_order_then_server_order()
    {
        var ordered = AccountIdentityRules.Ordered([I("a"), I("b"), I("c"), I("d")], ["c", "gone", "a"]);
        Assert.Equal("c,a,b,d", string.Join(',', ordered.Select(i => i.Id)));
    }
}
