// Mirrors: M3 plan section 6.4 (composer-ui step 2). What one composer needs from the rest of the app, handed over once so the view model has one constructor and tests fake each service. Owner composer-ui.

using Barbican.Core.Compose;
using Barbican.Core.Compose.Recipients;
using Barbican.Core.Plugins.Hooks;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Editor;

namespace Barbican.Presentation.Compose;

/// <summary>The services and settings of one composer.</summary>
public sealed record ComposerContext(
    IDraftService Drafts,
    IAttachmentUploader Uploader,
    IComposerSender Sender,
    IEditorViewModelFactory Editors,
    IRecipientSources Sources,
    IHookBus Hooks,
    TimeProvider Time,
    ComposeSettings Settings)
{
    public IToastService? Toasts { get; init; }

    /// <summary>Opens a seed in a (new) composer: undo send and "Cancel and edit" come back here.</summary>
    public Func<ComposeSeed, CancellationToken, Task>? Reopen { get; init; }

    public ITemplateStore? Templates { get; init; }

    /// <summary>Ask before a message goes outside the sender's own domains (Bulwark's external-recipient confirm is an organisation policy, off for a person: prefs <c>externalRecipientConfirm</c>, default false).</summary>
    public bool ConfirmExternalRecipients { get; init; }

    /// <summary><c>recipientMentionsEnabled</c> (G-075).</summary>
    public bool MentionsEnabled { get; init; } = true;

    /// <summary><c>mentionAddsRecipientTo</c>: "to", "cc", "bcc" or "none".</summary>
    public string MentionAddsRecipientTo { get; init; } = "to";
}
