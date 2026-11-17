// Mirrors: Rust msgload.rs TRAVEL :27 (the list position of the last message prefetched around, backwards, one row at a time; tag rust-final).
// Owner body.

namespace Barbican.Presentation.Mail.Loading;

/// <summary>Where the user last was in the list and which way they are going.</summary>
/// <param name="Last">The list index of the last message prefetched around.</param>
/// <param name="Back">Moving up the list (towards index 0).</param>
/// <param name="Moving">Moving one row at a time (key repeat): prefetch further ahead.</param>
public readonly record struct TravelState(int? Last, bool Back, bool Moving)
{
    /// <summary>Nothing opened yet.</summary>
    public static TravelState Start { get; } = new(null, false, false);
}
