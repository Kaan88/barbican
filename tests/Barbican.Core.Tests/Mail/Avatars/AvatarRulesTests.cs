// Ported from Rust src/avatars.rs tests (:377-:466).
// T-023 (D-234 note): the Rust test counted a worker out under the queue lock; the C# queue is a Channel reader and the same meaning is asserted:
// the busy count is zero after the queue drains.

using System.Text.Json.Nodes;
using Barbican.Core.Mail.Avatars;

namespace Barbican.Core.Tests.Mail.Avatars;

public sealed class AvatarRulesTests
{
    private static JsonNode J(string json) => JsonNode.Parse(json)!;

    [Fact]
    [RustTest("avatars::root_domains_and_validity")]
    public void Root_domains_and_validity()
    {
        Assert.Equal("example.com", AvatarRules.RootDomain("newsletter.example.com"));
        Assert.Equal("example.co.uk", AvatarRules.RootDomain("mail.shop.example.co.uk"));
        Assert.Equal("example.com", AvatarRules.RootDomain("Example.COM."));
        Assert.True(AvatarRules.ValidDomain("mail.example.org"));
        Assert.False(AvatarRules.ValidDomain("localhost"));
        Assert.False(AvatarRules.ValidDomain("printer.local"));
        Assert.False(AvatarRules.ValidDomain("a..b"));
        Assert.False(AvatarRules.ValidDomain("-x.com"));
        Assert.False(AvatarRules.ValidDomain("x_y.com"));
    }

    [Fact]
    [RustTest("avatars::candidate_order_follows_bulwark")]
    public void Candidate_order_follows_bulwark()
    {
        var all = new AvatarPrefs(Favicons: true, Gravatar: true, InJunk: false);
        var c = AvatarRules.Candidates("Jane@News.acme.example", "news.acme.example", all);
        Assert.Equal(3, c.Count);
        Assert.Equal("bimi:news.acme.example", c[0]);
        Assert.StartsWith("grav:", c[1], StringComparison.Ordinal);
        Assert.Equal("fav:acme.example", c[2]);

        // no logo without a DMARC pass for the From domain itself
        Assert.Equal(2, AvatarRules.Candidates("a@x.acme.example", "acme.example", all).Count);

        // personal providers get no favicon; off settings ask for nothing
        Assert.Empty(AvatarRules.Candidates("a@yahoo.com", null, new AvatarPrefs(Favicons: true, Gravatar: false, InJunk: false)));
        Assert.Empty(AvatarRules.Candidates("a@b.example", null, default));
        Assert.Empty(AvatarRules.Candidates("not-an-address", null, all));
    }

    [Fact]
    [RustTest("avatars::junk_and_off_show_nothing")]
    public void Junk_and_off_show_nothing()
    {
        var p = new AvatarPrefs(Favicons: true, Gravatar: false, InJunk: false);
        Assert.Null(AvatarRules.Get("a@b.example", null, p, junk: true));
        Assert.Null(AvatarRules.Get("a@b.example", null, default, junk: false));
    }

    [Fact]
    [RustTest("avatars::gravatar_hash_is_sha256_of_the_normalised_address")]
    public void Gravatar_hash_is_sha256_of_the_normalised_address()
    {
        Assert.Equal(AvatarRules.GravatarHash("test@example.com"), AvatarRules.GravatarHash(" Test@Example.com "));
        Assert.Equal(64, AvatarRules.GravatarHash("test@example.com").Length);
    }

    [Fact]
    [RustTest("avatars::bimi_records_and_dmarc")]
    public void Bimi_records_and_dmarc()
    {
        Assert.Equal("https://x.com/l.svg", AvatarRules.BimiLogoUrl("v=BIMI1; l=https://x.com/l.svg; a=https://x.com/c.pem"));
        Assert.Null(AvatarRules.BimiLogoUrl("v=BIMI1; l=http://x.com/l.svg"));
        Assert.Null(AvatarRules.BimiLogoUrl("v=spf1 include:x"));
        Assert.Equal("b.example", AvatarRules.DmarcPassDomain("mx.a; spf=pass smtp.mailfrom=b.example; dmarc=pass (p=reject) header.from=b.example"));
        Assert.Null(AvatarRules.DmarcPassDomain("mx.a; dmarc=fail header.from=b.example"));

        var m = J("""{"from": [{"email": "n@b.example"}], "headers": [{"name": "Authentication-Results", "value": "mx.mine.example; dmarc=pass header.from=b.example"}]}""");
        Assert.Equal("b.example", AvatarRules.BrandDomain(m, "mail.mine.example"));
        var spoof = J("""{"from": [{"email": "n@b.example"}], "headers": [{"name": "Authentication-Results", "value": "mx.mine.example; dmarc=pass header.from=evil.example"}]}""");
        Assert.Null(AvatarRules.BrandDomain(spoof, "mail.mine.example"));

        // a forged header with someone else's authserv-id earns no logo
        var forged = J("""{"from": [{"email": "n@bank.example"}], "headers": [{"name": "Authentication-Results", "value": "mx.bank.example; dmarc=pass header.from=bank.example"}]}""");
        Assert.Null(AvatarRules.BrandDomain(forged, "mail.mine.example"));
        Assert.True(AvatarRules.TrustedAuthserv("mail.mine.example; spf=pass", "mail.mine.example"));
        Assert.False(AvatarRules.TrustedAuthserv("; dmarc=pass", "mail.mine.example"));
        Assert.Equal("mail.mine.example", AvatarRules.HostOf("https://mail.mine.example:443/jmap"));

        // logo URLs only to public names
        foreach (var bad in new[] { "https://10.0.0.5/l.svg", "https://localhost/l.svg", "https://[::1]/l.svg", "https://x.local/l.svg", "https://a.example:8443/l.svg", "https://u:p@a.example/l.svg", "https://127.0.0.1/x" })
        {
            Assert.False(AvatarRules.SafeHttpsHost(bad), bad);
            Assert.Null(AvatarRules.BimiLogoUrl($"v=BIMI1; l={bad}"));
        }

        Assert.True(AvatarRules.SafeHttpsHost("https://brand.example.com/logo.svg"));
    }

    [Fact]
    [RustTest("avatars::idle_worker_is_counted_out_under_the_queue_lock")]
    public void Idle_worker_is_counted_out_after_the_queue_drains()
    {
        var q = new AvatarJobs();
        q.Reset(1);
        Assert.Null(q.NextJob());
        Assert.Equal(0, q.Busy);
    }

    [Fact]
    [RustTest("avatars::rasters_fit_a_square_and_reject_pixels")]
    public void Rasters_fit_a_square_and_reject_pixels()
    {
        var png = TestPng.Solid(32, 16, 1, 2, 3, 255);
        var b = AvatarRules.ToRaster(png)!;
        Assert.Equal((AvatarRules.Edge, AvatarRules.Edge), (b.Width, b.Height));
        Assert.Equal(AvatarRules.Edge * AvatarRules.Edge * 4, b.Rgba.Length);

        var dot = TestPng.Solid(1, 1, 0, 0, 0, 0);
        Assert.Null(AvatarRules.ToRaster(dot));
    }
}
