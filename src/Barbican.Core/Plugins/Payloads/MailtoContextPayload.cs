// Mirrors: Bulwark lib/plugin-hook-catalog.ts:103 (onMailtoIntercept: MailtoContext). Owner composer-ui.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>A mailto: link that is about to open the composer; a handler returning false stops it.</summary>
public sealed record MailtoContextPayload(string Url, IReadOnlyList<string> To, IReadOnlyList<string> Cc, IReadOnlyList<string> Bcc, string Subject, string Body);
