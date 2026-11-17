// Mirrors: M2 plan section 4.5 (IReaderBannerSource: banners are produced by sources, the reader runs their buttons). Added by composer-ui (additive): a banner source of another stream runs its own buttons through this seam,
// which ReaderBannerActions consults for an action id it does not know.

using Barbican.Presentation.Mail;

namespace Barbican.Presentation.Compose;

/// <summary>Runs the buttons of banners a stream added to the reader.</summary>
public interface IBannerActionHandler
{
    /// <summary>This handler owns the action.</summary>
    bool Owns(string actionId);

    /// <summary>Runs the action for the message on screen; the result is the line the reader shows.</summary>
    Task<string> RunAsync(ReaderSnapshot snapshot, string actionId, CancellationToken ct);
}
