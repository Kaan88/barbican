// Composition helper: one set of mail-action adapters per host (one undo-token table, one batch restore table), registered method by method in the app's container.
using Barbican.Core.Plugins.Bridge;

namespace Barbican.Core.Plugins.Api.MailActions;

/// <summary>The mail-action adapters of one host.</summary>
public sealed class PluginMailActionMethodSet(PluginMailActionHost host)
{
    public IReadOnlyList<IPluginApiMethod> Methods { get; } = PluginMailActionMethods.Create(host);
}
