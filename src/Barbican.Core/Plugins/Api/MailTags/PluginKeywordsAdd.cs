// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:1674-1705 doKeywordsAdd (settings:write, host-api.ts:244): appends missing definitions, never overwrites; ids
// are folded to lower case for the duplicate check; a missing colour is suggested; the add-on that added a tag owns it. Returns { added, skipped }.
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.MailTags;

internal sealed class PluginKeywordsAdd(PluginTagDefinitions tags) : PluginScopedMethod("keywords.add", "settings:write", mayMutate: true)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginKeywordArgs.TryDefinitions(Arg(arguments), forAdd: true, out _) ? null : PluginErrorCode.InvalidArguments;

    internal static PluginValue? Arg(PluginValue arguments) => arguments.Kind == PluginValueKind.Array && arguments.Count > 0 ? arguments.Item(0) : null;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        _ = PluginKeywordArgs.TryDefinitions(Arg(arguments), forAdd: true, out var inputs);
        if (!await BeginWriteAsync(context, ct).ConfigureAwait(false)) return Stale(context);
        var (added, skipped) = tags.Add(context.Binding.Package.Id, inputs);
        var b = new PluginValueBuilder();
        return Success(b, b.Object(("added", b.Array([.. added.Select(v => PluginKeywordArgs.Project(b, v))])), ("skipped", PluginKeywordArgs.Strings(b, skipped))));
    }
}
