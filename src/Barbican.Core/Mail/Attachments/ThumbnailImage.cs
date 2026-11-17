// Mirrors: Rust attachments.rs:62 thumbnail (image crate RGBA buffer). Stub from F2; owner mailcore.

namespace Barbican.Core.Mail.Attachments;

/// <summary>A decoded, scaled preview: RGBA pixels, row after row.</summary>
public sealed record ThumbnailImage(int Width, int Height, byte[] Rgba);
