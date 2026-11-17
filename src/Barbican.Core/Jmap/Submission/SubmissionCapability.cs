// Mirrors: RFC 8621 §1.3.2 (urn:ietf:params:jmap:submission: maxDelayedSend, submissionExtensions), read at the account level
// (J-004; Rust jmap.rs:306 account_cap, :712 max_delayed_send). Owner jmap.

using System.Text.Json;

namespace Barbican.Core.Jmap.Submission;

/// <summary>The submission capability of an account.</summary>
public sealed record SubmissionCapability
{
    /// <summary>The longest hold in seconds; 0 when the server cannot delay.</summary>
    public long MaxDelayedSend { get; init; }

    /// <summary>The SMTP extensions (object or array form), raw.</summary>
    public JsonElement? SubmissionExtensions { get; init; }
}
