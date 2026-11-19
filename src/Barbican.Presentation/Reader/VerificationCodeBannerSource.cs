// Mirrors: Bulwark lib/verification-code.ts:275 (the code of a sign-in mail) and components/email/email-viewer.tsx (the "Copy code" chip, only for a
// fresh message); Rust otp.rs:282 find, reader.rs (the chip for 24 hours after receipt). The code comes from the subject first, then the text part
// (VerificationCodeFinder, a port of otp.rs). Copying is the view's job (the clipboard is a platform service). Owner reader.

using System.Runtime.CompilerServices;
using System.Text;
using Barbican.Core.Mail.VerificationCode;
using Barbican.Presentation.Mail;
using Barbican.Core.Storage;

namespace Barbican.Presentation.Reader;

/// <summary>The verification-code chip of a fresh sign-in mail.</summary>
public sealed class VerificationCodeBannerSource(IVerificationCodeFinder finder, TimeProvider time, IPrefsStore? prefs = null) : IReaderBannerSource
{
    public const string Id = "code";
    public const string Copy = "code.copy";

    /// <summary>How long after receipt the chip is offered.</summary>
    public static readonly TimeSpan Fresh = TimeSpan.FromHours(24);

    private const int MaxText = 20_000;

    public bool Enabled => prefs?.Current.Bool("showVerificationCodes", true) ?? true;

    public static bool IsFresh(DateTimeOffset? received, DateTimeOffset now) =>
        received is { } at && now - at >= TimeSpan.FromMinutes(-5) && now - at <= Fresh;

    /// <summary>The code of a fresh message, or null (old, no text, no code).</summary>
    public string? CodeOf(ReaderSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!Enabled || snapshot.Body is not { } body || !IsFresh(body.ReceivedAt, time.GetUtcNow()))
        {
            return null;
        }

        var text = new StringBuilder();
        foreach (var part in body.TextBody)
        {
            if (part.PartId is { } id && body.BodyValues.TryGetValue(id, out var value) && text.Length < MaxText)
            {
                text.Append(value.Value).Append('\n');
            }
        }

        return finder.Find(body.Subject, text.Length > MaxText ? text.ToString(0, MaxText) : text.ToString());
    }

    public async IAsyncEnumerable<ReaderBanner> BannersForAsync(ReaderSnapshot snapshot, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        if (CodeOf(snapshot) is { } code)
        {
            yield return new ReaderBanner(Id, $"Verification code {code}", null, false, [(Copy, "Copy code")]);
        }
    }
}
