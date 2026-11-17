// Mirrors: Rust src/rte/mod.rs copy/paste (arboard: set_html, get().html(), get().file_list(), get_image(), get_text()). The seam that keeps tests off the desktop's real clipboard.

namespace Barbican.Presentation.Editor;

/// <summary>A file or picture on the clipboard.</summary>
public sealed record ClipboardFile(string Name, string Mime, byte[] Bytes);
