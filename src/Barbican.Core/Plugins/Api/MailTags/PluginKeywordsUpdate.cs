// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:1707-1730 doKeywordsUpdate (settings:write, host-api.ts:245): changes label, colour or visibility of definitions the
// calling add-on added itself; the id never changes; others are skipped. Returns { updated, skipped }.
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.MailTags;

internal sealed class PluginKeywordsUpdate(PluginTagDefinitions tags) : PluginScopedMethod("keywords.update", "settings:write", mayMutate: true)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginKeywordArgs.TryDefinitions(PluginKeywordsAdd.Arg(arguments), forAdd: false, out _) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        _ = PluginKeywordArgs.TryDefinitions(PluginKeywordsAdd.Arg(arguments), forAdd: false, out var patches);
        if (!await BeginWriteAsync(context, ct).ConfigureAwait(false)) return Stale(context);
        var (updated, skipped) = tags.Update(context.Binding.Package.Id, patches);
        var b = new PluginValueBuilder();
        return Success(b, b.Object(("updated", b.Array([.. updated.Select(v => PluginKeywordArgs.Project(b, v))])), ("skipped", PluginKeywordArgs.Strings(b, skipped))));
    }
}
