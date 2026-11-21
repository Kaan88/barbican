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
    /// <summary>The current profile display zone; the clock remains unchanged.</summary>
    public Func<TimeZoneInfo>? DisplayZone { get; init; }

    internal Func<ComposerDialog, Task<string?>>? AskInline { get; init; }

    public IToastService? Toasts { get; init; }

    /// <summary>Opens a seed in a (new) composer: undo send and "Cancel and edit" come back here.</summary>
    public Func<ComposeSeed, CancellationToken, Task>? Reopen { get; init; }

    public ITemplateStore? Templates { get; init; }

    /// <summary>The file nodes of the signed-in logins, for "Attach from Files" (null: no Files data in this build).</summary>
    public Barbican.Core.Files.IFileStore? Files { get; init; }

    /// <summary>The shared dialog host, for the attach-from-Files picker (null: no dialogs, and the menu entry does nothing).</summary>
    public Barbican.Presentation.Dialogs.IDialogService? Dialogs { get; init; }

    /// <summary>The Files setting <c>showHiddenFiles</c>: dot names in the picker (Bulwark's picker reads it at open, file-picker-dialog.tsx:56).</summary>
    public bool ShowHiddenFiles { get; init; }

    /// <summary>Creates the template picker view model of send-pipeline (null: the store is used directly).</summary>
    public Func<Pipeline.TemplatePickerViewModel>? TemplatePickers { get; init; }

    /// <summary>Ask before a message goes outside the sender's own domains (Bulwark's external-recipient confirm is an organisation policy, off for a person: prefs <c>externalRecipientConfirm</c>, default false).</summary>
    public bool ConfirmExternalRecipients { get; init; }

    /// <summary><c>recipientMentionsEnabled</c> (G-075).</summary>
    public bool MentionsEnabled { get; init; } = true;

    /// <summary><c>mentionAddsRecipientTo</c>: "to", "cc", "bcc" or "none".</summary>
    public string MentionAddsRecipientTo { get; init; } = "to";
}
