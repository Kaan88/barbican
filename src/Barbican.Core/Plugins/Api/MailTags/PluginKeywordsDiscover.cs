// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:1857-1865 doKeywordsDiscover (email:read, host-api.ts:250): the same message walk as jmap.getKeywords
// (limit 1-25000) with Stalwart's server-set attachment keywords left out. Delegates to Barbican's jmap.getKeywords adapter (Plugins/Api/Mail), which
// already drops those keywords, demands email:read for the captured login and rechecks authority after every page; only the method name differs.
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.MailTags;

internal sealed class PluginKeywordsDiscover(IPluginMailReaders readers) : IPluginApiMethod
{
    private readonly PluginJmapGetKeywords _walk = new(readers);

    public PluginApiDescriptor Descriptor { get; } = new("keywords.discover", [["email:read"]], TimeSpan.FromSeconds(120));

    public PluginErrorCode? ValidateArguments(PluginValue arguments) => _walk.ValidateArguments(arguments);

    public Task<PluginCallOutcome> InvokeAsync(IPluginInvocationContext context, PluginValue arguments, CancellationToken ct) => _walk.InvokeAsync(context, arguments, ct);
}
