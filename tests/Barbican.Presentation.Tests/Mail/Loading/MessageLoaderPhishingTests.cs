// Bulwark components/email/email-viewer.tsx:3310/3320 and __tests__/phishing-report-menus.test.tsx ("the reader"): a message with $phishing opens with its links
// turned off and the others are left alone. The page the reader builds is the body view's input (BodyRequest.Content).

#pragma warning disable MA0002 // asserts compare strings ordinally by nature
using Barbican.Core.Mail.Model;
using Barbican.Presentation.Mail;

namespace Barbican.Presentation.Tests.Mail.Loading;

public sealed class MessageLoaderPhishingTests
{
    private const string Html = "<p>Open <a href=\"https://evil.example.net/login\">https://bank.example.org</a> now.</p>";

    [Fact]
    public void A_message_with_the_phishing_mark_opens_with_its_links_turned_off()
    {
        using var h = new LoaderHarness();
        var body = LoaderHarness.Body("m1", Html) with { Keywords = Keywords.Of("$phishing", "$seen") };
        h.Cache.Put(h.Active.AccountKey!, body, h.Cache.Generation);

        h.Loader.Open(h.Row(1), OpenReason.Click, ListDirection.None);

        var shown = h.Presenter.LastRequest!.Content.Html!;
        Assert.DoesNotContain("href=", shown, StringComparison.Ordinal);
        Assert.DoesNotContain("evil.example.net", shown, StringComparison.Ordinal);
        Assert.Contains("data-link-off", shown, StringComparison.Ordinal);
        Assert.Contains("(link turned off)", shown, StringComparison.Ordinal);
        Assert.Contains("https://bank.example.org", shown, StringComparison.Ordinal);
    }

    [Fact]
    public void Other_messages_keep_their_links()
    {
        using var h = new LoaderHarness();
        h.Cache.Put(h.Active.AccountKey!, LoaderHarness.Body("m1", Html) with { Keywords = Keywords.Of("$seen", "$junk") }, h.Cache.Generation);

        h.Loader.Open(h.Row(1), OpenReason.Click, ListDirection.None);

        var shown = h.Presenter.LastRequest!.Content.Html!;
        Assert.Contains("href=\"https://evil.example.net/login\"", shown, StringComparison.Ordinal);
        Assert.DoesNotContain("data-link-off", shown, StringComparison.Ordinal);
    }
}
