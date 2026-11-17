// Mirrors: Rust tags.rs:139 Row. Stub from F2; owner mailcore.

namespace Barbican.Core.Mail.Tags;

/// <summary>A row of the sidebar's tag list: the tag, its depth and its chevron (0 none, 1 open, 2 closed).</summary>
public sealed record TagRow(TagDef Def, int Depth, int Chevron);
