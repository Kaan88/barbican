// Mirrors: Bulwark lib/plugin-sandbox/assistant/b.ts:153-158,178-182 (options object with only accountId, optional).
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Mail;

internal static class PluginEmailThreadOptions
{
    private static readonly string[] Allowed = ["accountId"];

    internal static bool TryParse(PluginValue? options, out string? accountId)
    {
        accountId = null;
        return PluginMailArgs.TryOptions(options, Allowed, false, out var o) && PluginMailArgs.TryAccount(PluginMailArgs.Prop(o, "accountId"), out accountId);
    }
}
