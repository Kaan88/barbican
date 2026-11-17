// Mirrors: Rust avatars.rs Pic (a fetched, decoded picture); M2 plan section 4.5 (IAvatarService.TryGet). Stub from stage F of M2; owner reader.

using Barbican.Core.Imaging;

namespace Barbican.Core.Mail.Avatars;

/// <summary>A resolved sender picture (a BIMI logo, a Gravatar or a favicon), already fitted to a square.</summary>
public sealed record AvatarImage(string Source, Raster Picture);
