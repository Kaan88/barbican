// Ported from Rust src/otp.rs tests (:287-:305).

using Barbican.Core.Mail.VerificationCode;

namespace Barbican.Core.Tests.Mail.VerificationCode;

public sealed class VerificationCodeFinderTests
{
    private static string? Find(string subject, string text) => new VerificationCodeFinder().Find(subject, text);

    [Fact]
    [RustTest("otp::finds_codes")]
    public void Finds_codes()
    {
        Assert.Equal("177945", Find("Your verification code is 177945", string.Empty));
        Assert.Equal("HUK6QR", Find("HUK6QR ist Ihr Bestätigungscode", string.Empty));
        Assert.Equal("557260", Find("Sign in", "Your code: 557 260. It expires in 10 minutes."));
        Assert.Equal("6ZA-BRE", Find("Sign-in", "Your OTP: 6ZA-BRE"));
        Assert.Equal("548980", Find("Welcome", "Der Bestätigungscode lautet 548980."));
    }

    [Fact]
    [RustTest("otp::skips_non_codes")]
    public void Skips_non_codes()
    {
        Assert.Null(Find("Promo code SAVE20 for 20% off", string.Empty));
        Assert.Null(Find("Order 123456 shipped", "Tracking code follows"));
        Assert.Null(Find("Meeting tomorrow", "Room 4021 at 10:30"));
        Assert.Null(Find("Claude Code 2026 release", string.Empty));
    }
}
