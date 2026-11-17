// Mirrors: Rust reader.rs plugin rows (the email.details rows of the native plugins under the subject: label, value, tone), spam.rs (the
// spam verdict row). M2 plan 4.5 (IEmailDetailsSource, the reader-below-subject slot). Owner unified.
// Until the reader renders IEmailDetailsSource rows in their own slot (request filed in FABLE-REVIEW "M2 stream: unified"), the rows come to
// the reader as banners: title = label, detail = value, warning for the danger and warning tones.

using System.Runtime.CompilerServices;
using Barbican.Core.Plugins.Native;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Reader;

namespace Barbican.Presentation.Unified;

/// <summary>The native plugins' detail rows of the open message, as reader banners.</summary>
public sealed class PluginDetailsBannerSource(NativePlugins plugins) : IReaderBannerSource
{
    /// <summary>The banner id prefix of a plugin row.</summary>
    public const string IdPrefix = "plugin-detail-";

    /// <inheritdoc />
    public async IAsyncEnumerable<ReaderBanner> BannersForAsync(ReaderSnapshot snapshot, [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Body is not { } body)
        {
            yield break;
        }

        var rows = await plugins.RowsForAsync(body, ct).ConfigureAwait(false);
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var warn = string.Equals(r.Tone, "danger", StringComparison.Ordinal) || string.Equals(r.Tone, "warning", StringComparison.Ordinal);
            yield return new ReaderBanner($"{IdPrefix}{i}", r.Label, r.Value, warn, []);
        }
    }
}
