// Composition helper: the mail-action adapters of one host (one undo-token table, one batch restore table), registered method by method in the app's container.
// email.forward, email.getAttachmentImage, search.*, tabs.* and the ui.* navigation methods are not part of this set (see docs/milestones/M6b-api-inventory.md).
using Barbican.Core.Plugins.Bridge;

namespace Barbican.Core.Plugins.Api.MailActions;

/// <summary>Factory for the mail-action methods.</summary>
public static class PluginMailActionMethods
{
    /// <summary>How many methods <see cref="Create"/> returns.</summary>
    public const int Count = 13;

    public static IReadOnlyList<IPluginApiMethod> Create(PluginMailActionHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return
        [
            new PluginEmailGetAttachment(host), new PluginEmailGetAttachmentText(host), new PluginEmailGetRaw(host),
            new PluginEmailMove(host, archive: false), new PluginEmailMove(host, archive: true),
            new PluginEmailSetKeyword(host, on: true), new PluginEmailSetKeyword(host, on: false), new PluginEmailUpdateKeywords(host),
            new PluginEmailBatch(host), new PluginEmailUndoBatch(host),
            new PluginDraftsCreate(host), new PluginComposeOpen(host), new PluginComposeReply(host),
        ];
    }
}
