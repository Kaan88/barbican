// Mirrors: Bulwark navigator.clipboard.writeText uses (copy sender address, copy file name, copy source); Rust ctxmenu.rs copy rows. The App registers the platform
// clipboard; tests use a recording fake. Owner reader.

namespace Barbican.Presentation.Common;

/// <summary>Puts text on the system clipboard.</summary>
public interface IClipboardService
{
    /// <summary>Replaces the clipboard with <paramref name="text"/>.</summary>
    void SetText(string text);
}
