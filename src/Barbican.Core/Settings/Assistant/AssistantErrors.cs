// Mirrors: Rust ai.rs:90-92 (no key), the status mapping of ai.rs (B-AI-003). Contract from the M6 plan section 4.4; owner set-data.

using Barbican.Core.Common;

namespace Barbican.Core.Settings.Assistant;

/// <summary>Plain messages for provider failures.</summary>
public static class AssistantErrors
{
    /// <summary>401 "key rejected", 403 "access denied", 404 "endpoint or model not found", 429 "rate limited" (with the retry-after seconds when sent), 503 and 529 "overloaded", any other status with the provider's own message when it sent one; the texts are the Rust ones.</summary>
    public static string Message(int status, TimeSpan? retryAfter, string? providerMessage) => throw new PortPendingException("AssistantErrors.Message");
}
