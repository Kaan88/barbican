// Mirrors: Rust ai.rs:90-92 (no key) and the status mapping of ai.rs post (B-AI-003). Contract from the M6 plan section 4.4; owner set-data.

using System.Globalization;

namespace Barbican.Plugin.Assistant;

/// <summary>Plain messages for provider failures.</summary>
public static class AssistantErrors
{
    /// <summary>What the user reads when no API key is stored (Rust ai.rs:90).</summary>
    public const string NoKey = "Add an API key in Settings → AI assistant first.";

    /// <summary>401 "key rejected", 403 "access denied", 404 "endpoint or model not found", 429 "rate limited" (with the retry-after seconds when sent), 503 and 529 "overloaded", any other status with the provider's own message when it sent one; the texts are the Rust ones.</summary>
    public static string Message(int status, TimeSpan? retryAfter, string? providerMessage)
    {
        var detail = providerMessage?.Trim() ?? string.Empty;
        return status switch
        {
            401 => "The API key was rejected (401).",
            403 => Join("Access denied (403).", detail),
            404 => Join("Endpoint or model not found (404). Check the address and model.", detail),
            429 => retryAfter is { } r ? $"Rate limited (429). Try again in {Math.Ceiling(r.TotalSeconds).ToString("0", CultureInfo.InvariantCulture)} s." : "Rate limited (429).",
            503 or 529 => "The AI service is overloaded. Try again shortly.",
            _ => $"AI service error {status.ToString(CultureInfo.InvariantCulture)}: {detail}",
        };
    }

    private static string Join(string text, string detail) => detail.Length == 0 ? text : text + " " + detail;
}
