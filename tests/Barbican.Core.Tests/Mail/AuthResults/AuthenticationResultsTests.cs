// Ported from Rust src/auth.rs tests (:93-:125). Red until the mailcore stream implements AuthenticationResults.

using Barbican.Core.Mail.AuthResults;

namespace Barbican.Core.Tests.Mail.AuthResults;

public sealed class AuthenticationResultsTests
{
    [Fact]
    [RustTest("auth::parses_and_aligns")]
    [Red("mailcore")]
    public void Parses_and_aligns()
    {
        var r = AuthenticationResults.Parse(
            "mx.example.org; dkim=pass (good sig) header.d=mail.shop.example.com header.s=s1; spf=pass smtp.mailfrom=bounce@other.example.net; dmarc=none header.from=shop.example.com");
        Assert.Equal(3, r.Count);
        Assert.Equal("mail.shop.example.com", r[0].Prop("header.d"));
        Assert.True(AuthenticationResults.AlignedPass(r, "news@shop.example.com"), "dkim d= is a child of the From domain");
        Assert.False(AuthenticationResults.AlignedPass(r, "ceo@bank.example.com"), "a pass for someone else's domain says nothing");
        Assert.True(AuthenticationResults.AlignedDkimPass(r, "shop.example.com"));
        Assert.False(AuthenticationResults.AlignedDkimPass(r, "evil.com"));
    }

    [Fact]
    [RustTest("auth::spf_needs_aligned_mailfrom")]
    [Red("mailcore")]
    public void Spf_needs_aligned_mailfrom()
    {
        var r = AuthenticationResults.Parse("mx; spf=pass smtp.mailfrom=a@attacker.example.net");
        Assert.False(AuthenticationResults.AlignedPass(r, "boss@corp.example.com"));
        Assert.True(AuthenticationResults.AlignedPass(r, "x@attacker.example.net"));
    }

    [Fact]
    [RustTest("auth::failures")]
    [Red("mailcore")]
    public void Failures()
    {
        Assert.True(AuthenticationResults.Failure(AuthenticationResults.Parse("mx; dmarc=fail"), true));
        Assert.True(AuthenticationResults.Failure(AuthenticationResults.Parse("mx; spf=softfail"), false));
        Assert.False(AuthenticationResults.Failure(AuthenticationResults.Parse("mx; spf=softfail; dkim=pass header.d=a.com"), true));
        Assert.False(AuthenticationResults.Failure(AuthenticationResults.Parse("mx; dkim=none"), false));
    }

    [Fact]
    [RustTest("auth::domains")]
    [Red("mailcore")]
    public void Domains()
    {
        Assert.Equal("example.com", AuthenticationResults.DomainOf("A@Example.COM."));
        Assert.Null(AuthenticationResults.DomainOf("nobody"));
        Assert.True(AuthenticationResults.Aligned("a.com", "b.a.com"));
        Assert.False(AuthenticationResults.Aligned("a.com", "ba.com"));
    }
}
