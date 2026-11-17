// email_dark.js is carried twice: as the App asset (diffable against the Rust app's src/email_dark.js) and as Core's string, which the page
// builder hashes for the frame's CSP. They must be the same text. Owner body.

using Barbican.Core.Mail.Body;

namespace Barbican.Core.Tests.Mail.Body;

public sealed class DarkMailScriptTests
{
    [Fact]
    public void Core_carries_the_asset_verbatim()
    {
        var asset = File.ReadAllText(TestRepo.PathOf("src/Barbican.App/Assets/mail/email_dark.js")).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(asset.TrimEnd('\n'), DarkMail.Script);
        Assert.DoesNotContain('\r', DarkMail.Script);
    }
}
