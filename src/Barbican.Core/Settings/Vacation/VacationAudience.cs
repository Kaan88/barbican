// Mirrors: Rust server_settings.rs (vacation get and set), sieve.rs vacation include; Bulwark stores/vacation-store.ts, lib/jmap/client.ts:5310-5347 (VacationResponse/get and set), lib/sieve/generator.ts:306 (VACATION_SCRIPT_NAME), vacation-audience.ts, vacation-forward.ts, components/settings/vacation-settings.tsx, vacation-calendar-block.tsx; RFC 8621 section 8; Stalwart crates/jmap/src/vacation/{get,set}.rs. G-021, G-022, I-435..I-437. Contract from the M6 plan section 4.7; owner srv-data.

namespace Barbican.Core.Settings.Vacation;

/// <summary>Who gets the auto-reply (Bulwark VacationAudience): only <c>internal</c> or <c>external</c> senders, relative to the listed own domains.</summary>
public sealed record VacationAudience(string Only, IReadOnlyList<string> Domains);
