// Mirrors: Bulwark lib/plugin-hook-catalog.ts:263-:265 (VacationView: isEnabled, fromDate, toDate, subject, textBody, htmlBody). Owner srv-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The out-of-office reply as plugins see it (Bulwark vacationToView).</summary>
public sealed record VacationHookPayload(bool IsEnabled, string? FromDate, string? ToDate, string Subject, string TextBody, string? HtmlBody);
