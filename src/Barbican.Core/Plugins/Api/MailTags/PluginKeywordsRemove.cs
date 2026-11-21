// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:1732-1751 doKeywordsRemove (settings:write, host-api.ts:246): removes definitions the calling add-on added itself;
// the keywords on messages stay (the UI's own delete does the same, TagService.Delete). Returns { removed, skipped }.
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.MailTags;

internal sealed class PluginKeywordsRemove(PluginTagDefinitions tags) : PluginScopedMethod("keywords.remove", "settings:write", mayMutate: true)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginKeywordArgs.TryIds(PluginKeywordsAdd.Arg(arguments), strict: false, out _) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        _ = PluginKeywordArgs.TryIds(PluginKeywordsAdd.Arg(arguments), strict: false, out var ids);
        if (!await BeginWriteAsync(context, ct).ConfigureAwait(false)) return Stale(context);
        var (removed, skipped) = tags.Remove(context.Binding.Package.Id, ids);
        var b = new PluginValueBuilder();
        return Success(b, b.Object(("removed", PluginKeywordArgs.Strings(b, removed)), ("skipped", PluginKeywordArgs.Strings(b, skipped))));
    }
}
