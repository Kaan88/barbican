// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:1665-1667 doKeywordsList (settings:read, host-api.ts:243): the tag definitions as [{ id, label, color, visibility?, addedBy? }].
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.MailTags;

internal sealed class PluginKeywordsList(PluginTagDefinitions tags) : PluginScopedMethod("keywords.list", "settings:read")
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => null;

    protected override Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        var b = new PluginValueBuilder();
        return Task.FromResult(Success(b, b.Array([.. tags.List().Select(v => PluginKeywordArgs.Project(b, v))])));
    }
}
