// Mirrors: Rust contacts_x.rs:565 shrink_photo (the media type and the JPEG bytes). Contract from the M5 plan section 4.1; owner con-data.

namespace Barbican.Core.Contacts;

/// <summary>A picked photo after shrinking: always image/jpeg.</summary>
#pragma warning disable CA1819 // a record of raw image bytes: the array is the data
public sealed record ShrunkPhoto(string MediaType, byte[] Bytes);
#pragma warning restore CA1819
