// Mirrors: Bulwark 1.21.2 components/email/reader-selection-menu.tsx:190-195 (the rows of the `reader-selection-menu` surface). RD-08. Owner reader.

using Barbican.Core.Common;

namespace Barbican.Presentation.Reader;

/// <summary>The add-on side of the selection menu: the rows now, and running one on the selected text. The App supplies it where add-ons exist.</summary>
public interface IReaderSelectionAddOns
{
    IReadOnlyList<ReaderSelectionAddOn> Current();

    Task<Result<Unit>> RunAsync(string key, string text, CancellationToken ct);
}
