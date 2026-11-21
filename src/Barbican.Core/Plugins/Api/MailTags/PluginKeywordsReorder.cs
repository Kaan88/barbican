// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:1753-1810 doKeywordsReorder (settings:write, host-api.ts:247): ids must name every existing definition exactly once
// (case-insensitively unless options.caseSensitive is true); any other option is refused. Bulwark throws a descriptive error for a wrong list; here it is
// InvalidArguments. Reuses the existing definitions verbatim, so ordering cannot change a label, colour or owner. Returns the new list.
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.MailTags;

internal sealed class PluginKeywordsReorder(PluginTagDefinitions tags) : PluginScopedMethod("keywords.reorder", "settings:write", mayMutate: true)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginKeywordArgs.TryIds(PluginKeywordsAdd.Arg(arguments), strict: false, out _) && TryOptions(arguments, out _) ? null : PluginErrorCode.InvalidArguments;

    private static bool TryOptions(PluginValue arguments, out bool caseSensitive)
    {
        caseSensitive = false;
        var options = PluginMailArgs.At(arguments, 1);
        if (options is null) return true;
        if (!PluginMailArgs.TryOptions(options, ["caseSensitive"], required: true, out var o)) return false;
        if (PluginMailArgs.Prop(o, "caseSensitive") is not { } flag) return true;
        if (flag.Kind != PluginValueKind.Boolean) return false;
        caseSensitive = flag.BooleanValue;
        return true;
    }

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        _ = PluginKeywordArgs.TryIds(PluginKeywordsAdd.Arg(arguments), strict: false, out var ids);
        _ = TryOptions(arguments, out var caseSensitive);
        if (!await BeginWriteAsync(context, ct).ConfigureAwait(false)) return Stale(context);
        if (tags.Reorder(ids, caseSensitive) is not { } ordered) return PluginCallOutcome.Failed(PluginErrorCode.InvalidArguments);
        var b = new PluginValueBuilder();
        return Success(b, b.Array([.. ordered.Select(v => PluginKeywordArgs.Project(b, v))]));
    }
}
