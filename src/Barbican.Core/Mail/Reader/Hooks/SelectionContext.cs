// Mirrors: Bulwark lib/plugin-hook-catalog.ts:328 onTextSelectionChange (SelectionContext: text, emailId); debounced in the body shell. Owner reader.

namespace Barbican.Core.Mail.Reader.Hooks;

/// <summary>The text selected in the message body (empty when the selection was cleared).</summary>
public sealed record SelectionContext(string EmailId, string Text);
