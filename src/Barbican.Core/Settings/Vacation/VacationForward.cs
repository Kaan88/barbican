// Mirrors: Rust server_settings.rs (vacation get and set), sieve.rs vacation include; Bulwark stores/vacation-store.ts, lib/jmap/client.ts:5310-5347 (VacationResponse/get and set), lib/sieve/generator.ts:306 (VACATION_SCRIPT_NAME), vacation-audience.ts, vacation-forward.ts, components/settings/vacation-settings.tsx, vacation-calendar-block.tsx; RFC 8621 section 8; Stalwart crates/jmap/src/vacation/{get,set}.rs. G-021, G-022, I-435..I-437. Contract from the M6 plan section 4.7; owner srv-data.

namespace Barbican.Core.Settings.Vacation;

/// <summary>Forward while away; it runs in the vacation's period (Bulwark withVacationPeriod). KeepCopy is "keep in inbox".</summary>
public sealed record VacationForward(bool Enabled, string To, bool KeepCopy);
