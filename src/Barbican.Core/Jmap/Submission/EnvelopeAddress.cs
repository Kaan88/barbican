// Mirrors: RFC 8621 §7 (Envelope Address: email plus SMTP parameters, e.g. HOLDFOR). Stub from F2; owner jmap.

namespace Barbican.Core.Jmap.Submission;

/// <summary>An SMTP envelope address.</summary>
public sealed record EnvelopeAddress(string Email, IReadOnlyDictionary<string, string>? Parameters = null);
