// Mirrors: Bulwark 1.21.2 components/email/reader-selection-menu.tsx:190-195 (the rows of the `reader-selection-menu` surface). RD-08. Owner reader.

namespace Barbican.Presentation.Reader;

/// <summary>One row an add-on put in the reader's selection menu (<c>api.ui.setActions('reader-selection-menu', …)</c>).</summary>
public sealed record ReaderSelectionAddOn(string Key, string Label, string? Icon, bool Danger);
