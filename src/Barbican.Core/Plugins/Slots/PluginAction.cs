// Mirrors: Bulwark lib/plugin-sandbox/runtime.tsx ui.setActions declarative action schema.
using System.Collections.Immutable;
namespace Barbican.Core.Plugins.Slots;
public sealed record PluginAction(string Id, string Label, string? Icon, double Order, bool Danger,
    ImmutableArray<string> MailboxRoles, ImmutableArray<string> MimeTypes, ImmutableArray<string> AccountKinds,
    string? HasKeyword = null, string? NotKeyword = null);
