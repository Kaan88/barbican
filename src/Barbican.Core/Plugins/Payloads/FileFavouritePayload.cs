// Mirrors: Bulwark lib/plugin-hook-catalog.ts:230 onFileFavoriteToggle { file, favorite }. Owner files-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>onFileFavoriteToggle: the file and whether it is now starred.</summary>
public sealed record FileFavouritePayload(FileResourceView File, bool Favorite);
