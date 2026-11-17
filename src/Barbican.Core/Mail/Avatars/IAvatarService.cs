// Mirrors: M2 plan section 4.5 (IAvatarService); Rust avatars.rs:122 get; hook onAvatarResolve (hooks.csv, owner reader). Stub contract from stage F of M2; owner reader.

namespace Barbican.Core.Mail.Avatars;

/// <summary>Sender pictures: cached, fetched in the background through IGuardedHttp, never on the UI thread.</summary>
public interface IAvatarService
{
    /// <summary>The picture for a sender when one is ready, else null (a fetch is queued when the settings allow one).</summary>
    AvatarImage? TryGet(string email, string? brandDomain, bool junk);

    /// <summary>Raised when a queued picture is ready.</summary>
    event EventHandler<AvatarArrivedEventArgs>? Arrived;
}
