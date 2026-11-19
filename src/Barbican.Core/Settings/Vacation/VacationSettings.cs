// Mirrors: Rust server_settings.rs (vacation get and set), sieve.rs vacation include; Bulwark stores/vacation-store.ts, lib/jmap/client.ts:5310-5347 (VacationResponse/get and set), lib/sieve/generator.ts:306 (VACATION_SCRIPT_NAME), vacation-audience.ts, vacation-forward.ts, components/settings/vacation-settings.tsx, vacation-calendar-block.tsx; RFC 8621 section 8; Stalwart crates/jmap/src/vacation/{get,set}.rs. G-021, G-022, I-435..I-437. Contract from the M6 plan section 4.7; owner srv-data.

namespace Barbican.Core.Settings.Vacation;

/// <summary>Everything the out-of-office page edits. Dates are days (UTC boundaries in the script and in VacationResponse); BlockCalendar adds the calendar out-of-office block (G-022).</summary>
public sealed record VacationSettings(bool Enabled, string Subject, string TextBody, string? HtmlBody, DateOnly? From, DateOnly? To, VacationAudience? Audience, VacationForward? Forward, bool BlockCalendar)
{
    /// <summary>The VacationResponse/get state captured with these settings.</summary>
    public string? State { get; init; }

    /// <summary>The SieveScript/get state captured with the vacation extras.</summary>
    public string? SieveState { get; init; }
}
