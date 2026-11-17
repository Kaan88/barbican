// Mirrors: M2 plan section 4.5 (IReaderBannerSource; multi-registration). Contract from stage F of M2; owner reader.

using Barbican.Presentation.Mail;

namespace Barbican.Presentation.Reader;

/// <summary>Produces the banners of the open message (reader provides several; plugins may add more in M6b).</summary>
public interface IReaderBannerSource
{
    IAsyncEnumerable<ReaderBanner> BannersForAsync(ReaderSnapshot snapshot, CancellationToken ct);
}
