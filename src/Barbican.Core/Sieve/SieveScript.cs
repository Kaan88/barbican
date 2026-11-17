// Mirrors: Rust sieve.rs:10 EXTERNAL, :25 parse, :175 scan_top_level, :239 vacation_only, :399 period_boundary, :432 period_tests, :478 generate, :483 with_rules,
// :492 generate_meta, :613 rule, :626 day_boundary; Bulwark lib/sieve/{parser,generator,period}.ts; RFC 5228 (Sieve), RFC 5229 (variables is not used),
// RFC 5230 (vacation), RFC 5260 (date), RFC 5231 (relational), RFC 5435 (spamtest); Stalwart crates/jmap/src/sieve/{get,set,validate}.rs.
// M6 rows (T-271..T-279) ported early because quick rules call the parser and the generator (M2 plan 2.1). Stub from stage F of M2; owner search (wave 2).

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Sieve;

/// <summary>The Sieve parser and generator over Bulwark's metadata block.</summary>
public static class SieveScript
{
    /// <summary>The marker line above rules that are not Bulwark's.</summary>
    public const string External = "# --- External rules (managed outside Bulwark) ---";

    /// <summary>Splits a script into the metadata, the external part and the locked flag; a vacation-only script becomes the auto-reply.</summary>
    public static SieveParsed Parse(string script)
    {
        throw new PortPendingException("sieve.rs:25 parse");
    }

    /// <summary>The top-level blocks of a script body: if/elsif/else chains stay whole, text: strings with a leading # are not comments.</summary>
    public static IReadOnlyList<SieveBlock> ScanTopLevel(string content)
    {
        throw new PortPendingException("sieve.rs:175 scan_top_level");
    }

    /// <summary>The vacation settings of a script that contains nothing but a vacation action (no test), else null.</summary>
    public static JsonNode? VacationOnly(string content)
    {
        throw new PortPendingException("sieve.rs:239 vacation_only");
    }

    /// <summary>The Sieve text for rules plus an external part (metadata version 1 shape).</summary>
    public static string Generate(IReadOnlyList<JsonNode> rules, string external)
    {
        throw new PortPendingException("sieve.rs:478 generate");
    }

    /// <summary>The metadata with its rules replaced, extras (vacation, forward, audience) kept.</summary>
    public static JsonNode WithRules(JsonNode metadata, IReadOnlyList<JsonNode> rules)
    {
        throw new PortPendingException("sieve.rs:483 with_rules");
    }

    /// <summary>The Sieve text for the whole metadata (rules, vacation, forward, audience, period tests) given the server's extensions.</summary>
    public static string GenerateMeta(JsonNode metadata, string external, IReadOnlyList<string> serverExtensions)
    {
        throw new PortPendingException("sieve.rs:492 generate_meta");
    }

    /// <summary>A rule in Bulwark's shape with one condition and one action.</summary>
    public static JsonNode Rule(string name, string field, string comparator, string value, string action, string actionValue, bool stop)
    {
        throw new PortPendingException("sieve.rs:613 rule");
    }

    /// <summary>The UTC date and time tests for activeFrom and activeUntil (none for an empty object), or null when one is not a valid timestamp.</summary>
    public static IReadOnlyList<string>? PeriodTests(JsonNode period)
    {
        throw new PortPendingException("sieve.rs:432 period_tests");
    }

    /// <summary>The UTC (date, time) of an ISO timestamp with seconds and a zone, or null.</summary>
    public static PeriodBoundary? PeriodBoundaryOf(string value)
    {
        throw new PortPendingException("sieve.rs:399 period_boundary");
    }

    /// <summary>The UTC ISO instant (.000Z) of a form day: the start of the day, or its end; empty gives a null value, a bad format a failure.</summary>
    public static Result<string?> DayBoundary(string day, bool end)
    {
        throw new PortPendingException("sieve.rs:626 day_boundary");
    }
}
