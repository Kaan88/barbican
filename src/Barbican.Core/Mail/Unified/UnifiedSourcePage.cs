// Mirrors: Rust unified.rs:296 SourcePage (the rows of one source with its total, conversation sizes and inbox unread). Owner unified.

using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Unified;

/// <summary>One page of one source, its rows already carrying unified row ids.</summary>
/// <param name="Key">The source key.</param>
/// <param name="Rows">The rows, newest first.</param>
/// <param name="Total">The source's inbox total (for the query).</param>
/// <param name="Threads">Conversation sizes by unified thread id.</param>
/// <param name="Unread">The source's inbox unread.</param>
/// <param name="Warning">A nonfatal source ordering limitation.</param>
public sealed record UnifiedSourcePage(string Key, IReadOnlyList<EmailSummary> Rows, int Total, IReadOnlyDictionary<string, int> Threads, long Unread, string? Warning = null);
