// RFC 7636 §4.1-§4.2 and Appendix B (the worked S256 example).

using Barbican.Core.Auth;

namespace Barbican.Core.Tests.Auth;

public sealed class PkceTests
{
    [Fact]
    public void Challenge_matches_the_rfc_example()
    {
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", Pkce.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
    }

    [Fact]
    public void Verifier_and_state_are_url_safe_and_fresh()
    {
        var v = Pkce.NewVerifier();
        Assert.InRange(v.Length, 43, 128);
        Assert.All(v, c => Assert.True(char.IsAsciiLetterOrDigit(c) || c is '-' or '_', $"bad char {c}"));
        Assert.NotEqual(v, Pkce.NewVerifier(), StringComparer.Ordinal);
        Assert.NotEqual(Pkce.NewState(), Pkce.NewState(), StringComparer.Ordinal);
    }
}
