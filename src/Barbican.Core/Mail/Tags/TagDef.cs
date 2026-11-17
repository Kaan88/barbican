// Mirrors: Rust tags.rs:9 TagDef; Bulwark settings-store emailKeywords ({id, label, color}). Stub from F2; owner mailcore.

namespace Barbican.Core.Mail.Tags;

/// <summary>A user tag: id (the keyword is <c>$label:id</c>), label and colour name or <c>#rrggbb</c>.</summary>
public sealed record TagDef(string Id, string Label, string Color);
