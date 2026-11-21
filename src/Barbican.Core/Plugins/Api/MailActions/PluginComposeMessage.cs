// Mirrors: Bulwark lib/plugin-sandbox/host-api-compose.ts:24-62 readMessage (to, cc, bcc, subject, text, sanitised html, blob references). Everything here is already checked and cleaned by the adapter.
namespace Barbican.Core.Plugins.Api.MailActions;

/// <summary>A message an add-on wants written: recipients, subject and body as plain text and, when the add-on sent HTML, as sanitised HTML.</summary>
public sealed record PluginComposeMessage(
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> Bcc,
    string Subject,
    string Text,
    string? Html,
    IReadOnlyList<PluginBlobRef> Attachments);
