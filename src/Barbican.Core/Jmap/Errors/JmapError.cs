// Mirrors: Bulwark lib/jmap/jmap-error.ts; RFC 8620 §3.6 (errors); M1-plan §4.1. Contract stub from F2; owner jmap.

namespace Barbican.Core.Jmap.Errors;

/// <summary>Base of every expected failure of a JMAP call.</summary>
/// <param name="Message">What to show or log.</param>
public abstract record JmapError(string Message);
