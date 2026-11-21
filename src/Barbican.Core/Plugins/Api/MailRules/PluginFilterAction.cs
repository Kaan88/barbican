// Mirrors: Bulwark lib/plugin-sandbox/assistant/c.filters.ts:140-170 readAction (the parts of an action an add-on may name: its type, a folder of move and copy, a tag of add_label).
namespace Barbican.Core.Plugins.Api.MailRules;

/// <summary>One action of an add-on's rule as it was written, before the folder and the tag are looked up.</summary>
internal sealed record PluginFilterAction(string Type, string? MailboxId, string? TagId);
