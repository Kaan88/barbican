// Mirrors: M3 plan section 4.7 (DI skeleton). Owners: editor-view (IEditorViewModelFactory, IRewriteProvider) and composer-ui (IComposerService, IRecipientSources); each stream edits only its own lines.

using Barbican.Core.Accounts;
using Barbican.Core.Compose;
using Barbican.Core.Compose.Recipients;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Storage;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Common;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Reader;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Compose.Pipeline;
using Barbican.Presentation.Editor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.Presentation.Composition;

/// <summary>The portable (UI-free) services of the M3 streams editor-view and composer-ui.</summary>
public static class M3PresentationRegistration
{
    public static IServiceCollection AddBarbicanEditorViewModels(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IEditorViewModelFactory, EditorViewModelFactory>();
        services.TryAddSingleton<IRewriteProvider, NoRewriteProvider>();
        return services;
    }

    public static IServiceCollection AddBarbicanComposerViewModels(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        // the send flow of send-pipeline (checks, the undo toast with Undo and Send now, scheduled toasts) behind the composer's one seam; Lazy because SendCoordinator opens seeds through IComposerService
        services.TryAddSingleton<IComposerSender>(sp => new CoordinatorComposerSender(new Lazy<SendCoordinator>(sp.GetRequiredService<SendCoordinator>), sp.GetRequiredService<IScheduledSends>(), sp.GetRequiredService<IMailSender>()));

        // autocomplete: the people written to, from Sent, now; the contact cards join through the same interface in M5 (D-245)
        services.Replace(ServiceDescriptor.Singleton<IRecipientSources>(sp => sp.GetService<IAccountManager>() is { } accounts
            ? new SentRecipientSources(accounts, sp.GetRequiredService<TimeProvider>())
            : new EmptyRecipientSources()));
        services.TryAddSingleton(sp => new ComposerServices(
            sp.GetRequiredService<IComposeSeeder>(),
            sp.GetRequiredService<IIdentityService>(),
            sp.GetRequiredService<IDraftService>(),
            sp.GetRequiredService<IAttachmentUploader>(),
            sp.GetRequiredService<IComposerSender>(),
            sp.GetRequiredService<IEditorViewModelFactory>(),
            sp.GetRequiredService<IRecipientSources>(),
            sp.GetRequiredService<IHookBus>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IPrefsStore>(),
            sp.GetService<IDispatcher>() ?? new InlineDispatcher())
        {
            Toasts = sp.GetService<IToastService>(),
            Templates = sp.GetService<ITemplateStore>(),
            TemplatePickers = () => sp.GetService<TemplatePickerViewModel>() ?? throw new InvalidOperationException("The send pipeline view models are not registered."),
            MessageOpen = () => sp.GetService<ReaderViewModel>() is { } reader && reader.Current.EmailId.Length > 0,
        });
        services.Replace(ServiceDescriptor.Singleton<IComposerService>(sp => new ComposerService(sp.GetRequiredService<ComposerServices>())));
        services.TryAddSingleton(sp => new MailtoRouter(
            sp.GetRequiredService<IComposerService>(),
            sp.GetService<IAccountManager>(),
            sp.GetRequiredService<IHookBus>(),
            sp.GetService<IDialogService>(),
            sp.GetService<IToastService>(),
            () => sp.GetService<IActiveMail>()?.AccountKey));

        // the draft banner above a message in Drafts (Edit, Discard); its buttons run through the reader's banner actions
        services.TryAddSingleton(sp => new DraftBannerSource(sp.GetRequiredService<IComposerService>(), sp.GetRequiredService<IDraftService>(), sp.GetRequiredService<IActiveMail>(), sp.GetService<IToastService>()));
        services.AddSingleton<IReaderBannerSource>(sp => sp.GetRequiredService<DraftBannerSource>());
        services.AddSingleton<IBannerActionHandler>(sp => sp.GetRequiredService<DraftBannerSource>());

        // the commands that open and steer the composer
        services.TryAddSingleton(sp => new ComposerCommandSet(ComposerCommands.Create(sp.GetRequiredService<IComposerService>(), sp.GetRequiredService<IActiveMail>(), sp.GetService<IAccountManager>())));
        foreach (var index in Enumerable.Range(0, ComposerCommands.Count))
        {
            var i = index;
            services.AddSingleton<IAppCommand>(sp => sp.GetRequiredService<ComposerCommandSet>().Commands[i]);
        }

        return services;
    }
}
