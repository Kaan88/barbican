// Mirrors: Bulwark components/email/phishing-mark-banner.tsx (role="note" strip above a message with $phishing: "You reported this message as phishing. Its
// links are turned off.") and lib/phishing-links.ts isPhishingMarked :13. The links themselves are turned off by the loader (MessageLoader, PhishingLinks).
// Owner reader.

using System.Runtime.CompilerServices;
using Barbican.Core.Mail.Safety;
using Barbican.Presentation.Mail;

namespace Barbican.Presentation.Reader;

/// <summary>The banner of a message the user reported as phishing.</summary>
public sealed class PhishingMarkBannerSource : IReaderBannerSource
{
    public const string Id = "phishing-mark";

    /// <summary>The line of the banner and the tooltip of a turned-off link (locales/en/common.json phishing_report.banner, link_off).</summary>
    public const string Text = "You reported this message as phishing. Its links are turned off.";

    /// <summary>The tooltip of a turned-off link and the note a screen reader hears after its text (phishing_report.link_off, link_off_note).</summary>
    public const string LinkOffTitle = "link turned off";

    public const string LinkOffNote = "(link turned off)";

    public async IAsyncEnumerable<ReaderBanner> BannersForAsync(ReaderSnapshot snapshot, [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await Task.CompletedTask.ConfigureAwait(false);
        if (snapshot.Body is { } body && PhishingLinks.IsMarked(body.Keywords))
        {
            yield return new ReaderBanner(Id, Text, null, true, []);
        }
    }
}
