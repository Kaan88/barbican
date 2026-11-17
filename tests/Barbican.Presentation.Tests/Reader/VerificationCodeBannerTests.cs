// OTP chips (M2 plan §6.3 step 8): the code of a sign-in mail is offered for 24 hours after receipt, from the subject first, then the text part.

using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.VerificationCode;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Reader;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Presentation.Tests.Reader;

public sealed class VerificationCodeBannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_fresh_sign_in_mail_offers_its_code_for_24_hours()
    {
        var clock = new FakeTimeProvider(Now);
        var source = new VerificationCodeBannerSource(new VerificationCodeFinder(), clock);
        var body = new EmailBody
        {
            Id = "c1",
            Subject = "Your sign-in code",
            ReceivedAt = Now.AddMinutes(-5),
            TextBody = [new BodyPart { PartId = "1", Type = "text/plain" }],
            BodyValues = new Dictionary<string, BodyValue>(StringComparer.Ordinal) { ["1"] = new BodyValue("Your verification code is 482913. It expires in 10 minutes.") },
        };
        var snapshot = ReaderSnapshots.FromBody(body, null, new ReaderOptions());
        Assert.Equal("482913", source.CodeOf(snapshot));

        clock.Advance(TimeSpan.FromHours(24));
        Assert.Null(source.CodeOf(snapshot));
        Assert.Null(source.CodeOf(ReaderSnapshots.FromBody(body with { TextBody = [] }, null, new ReaderOptions())));
    }
}
