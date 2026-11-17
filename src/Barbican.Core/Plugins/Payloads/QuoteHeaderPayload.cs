// Mirrors: Bulwark lib/plugin-hook-catalog.ts:97 onBuildQuoteHeader (QuoteHeader, QuoteHeaderContext). Owner send-pipeline.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The line above a quoted original ("On Tue, 17 Nov 2026, 08:00, Ada wrote:") and what it was built from; a transform handler returns it with another <see cref="Header"/>.</summary>
public sealed record QuoteHeaderPayload(string Header, string Kind, string SenderName, string SenderEmail, DateTimeOffset? SentAt, string Subject);
