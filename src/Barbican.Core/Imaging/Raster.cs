// Mirrors: M2 plan section 4.5 (Core/Raster: Raster(int Width, int Height, byte[] Rgba) for avatars, T-024); Rust avatars.rs:281 raster (a SharedPixelBuffer of Rgba8Pixel).
// Stub from stage F of M2; owner reader.

namespace Barbican.Core.Imaging;

/// <summary>A decoded picture: RGBA, 4 bytes per pixel, rows top to bottom.</summary>
#pragma warning disable CA1819 // a record of raw pixels: the array is the data
public sealed record Raster(int Width, int Height, byte[] Rgba);
#pragma warning restore CA1819
