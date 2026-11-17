// Mirrors: M3 plan section 4.7 (the composer's services resolve from DI). The dependencies of ComposerService in one record so the registration builds it once and tests fake each member. Owner composer-ui.

using Barbican.Core.Compose;
using Barbican.Core.Compose.Recipients;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Storage;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Common;
using Barbican.Presentation.Editor;

namespace Barbican.Presentation.Compose;

/// <summary>What the composer service needs.</summary>
public sealed record ComposerServices(
    IComposeSeeder Seeder,
    IIdentityService Identities,
    IDraftService Drafts,
    IAttachmentUploader Uploader,
    IComposerSender Sender,
    IEditorViewModelFactory Editors,
    IRecipientSources Sources,
    IHookBus Hooks,
    TimeProvider Time,
    IPrefsStore Prefs,
    IDispatcher Dispatcher)
{
    public IToastService? Toasts { get; init; }

    public ITemplateStore? Templates { get; init; }

    public Func<Pipeline.TemplatePickerViewModel>? TemplatePickers { get; init; }

    /// <summary>A message is open in the reader (a reply docks under it, <see cref="ComposerDock"/>).</summary>
    public Func<bool> MessageOpen { get; init; } = () => false;
}
