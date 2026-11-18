// Mirrors: M2 plan section 3.3. Owner reader: IGuardedHttp, IAvatarService, IVerificationCodeFinder, IAttachmentSaver, ITrustedSenders, IReaderBannerSource (several), MENU-READER and MENU-ATT providers,
// the attachment and reader commands, the write-test guard.

using Barbican.App.MailBody;
using Barbican.Core.Accounts;
using Barbican.Core.Mail.Attachments;
using Barbican.Core.Mail.AuthResults;
using Barbican.Core.Mail.Avatars;
using Barbican.Core.Mail.Body;
using Barbican.Core.Mail.Mdn;
using Barbican.Core.Mail.Trust;
using Barbican.Core.Net;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Storage;
using Barbican.Platform;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Mail.Loading;
using Barbican.Presentation.Reader;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.App.Composition.Features;

/// <summary>Reader depth: unsubscribe, receipts, invitations, trust, source, attachments, avatars, guarded HTTP (M2 stream reader).</summary>
public static class ReaderRegistration
{
    public static IServiceCollection AddBarbicanReaderDepth(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IGuardedHttp, GuardedHttp>();
        services.AddSingleton(sp => new AuthServPins(sp.GetRequiredService<IProfilePaths>().DataDir));
        services.Replace(ServiceDescriptor.Singleton<ITrustedSenders>(sp => new TrustedSenders(sp.GetRequiredService<IProfilePaths>().DataDir, sp.GetService<Barbican.Core.Plugins.Hooks.IHookBus>())));
        services.AddSingleton(sp => new MdnSender(sp.GetRequiredService<TimeProvider>()));

        // links from messages and banners: the plugins' link check, then the system browser (M1's LoggingLinkRouter opened nothing)
        services.AddSingleton(sp =>
        {
            var browser = sp.GetRequiredService<IBrowserLauncher>();
            return new CheckedLinkRouter(sp.GetRequiredService<IHookBus>(), browser.OpenAsync);
        });
        services.Replace(ServiceDescriptor.Singleton<ILinkRouter>(sp => sp.GetRequiredService<CheckedLinkRouter>()));

        // the banner stack, in this order above the body
        services.AddSingleton<IReaderBannerSource, InvitationBannerSource>();
        services.AddSingleton<IReaderBannerSource, ReceiptBannerSource>();
        services.AddSingleton<IReaderBannerSource, UnsubscribeBannerSource>();
        services.Replace(ServiceDescriptor.Singleton<Barbican.Core.Mail.VerificationCode.IVerificationCodeFinder, Barbican.Core.Mail.VerificationCode.VerificationCodeFinder>());
        services.AddSingleton<IReaderBannerSource, VerificationCodeBannerSource>();
        services.AddSingleton<ReaderBannerActions>();
        services.AddSingleton(sp => new ReaderDepth(sp.GetRequiredService<AuthServPins>(), sp.GetServices<IReaderBannerSource>(), sp.GetRequiredService<ITrustedSenders>()));

        // saving: the account's blobs to a local file (Downloads, or a queued answer in a hidden run)
        services.AddSingleton<SaveTargets>();
        services.AddSingleton(sp => new AttachmentSaver(
            sp.GetRequiredService<IAccountManager>(), sp.GetRequiredService<IHookBus>(), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<SaveTargets>().PickAsync));
        services.Replace(ServiceDescriptor.Singleton<IAttachmentSaver>(sp => sp.GetRequiredService<AttachmentSaver>()));

        // avatars: off by default (S-senderFavicons, S-gravatarAvatars); GuardedHttp only; BIMI TXT through DnsClient; a disk cache under the profile
        services.AddSingleton(sp =>
        {
            var prefs = sp.GetRequiredService<IPrefsStore>();
            var dns = new DnsClient.LookupClient();
            return new AvatarService(
                sp.GetRequiredService<IGuardedHttp>(),
                () => new AvatarPrefs(prefs.Current.Bool("senderFavicons", false), prefs.Current.Bool("gravatarAvatars", false), prefs.Current.Bool("showAvatarsInJunk", false)),
                sp.GetRequiredService<IHookBus>(),
                sp.GetRequiredService<TimeProvider>(),
                async (name, ct) => (await dns.QueryAsync(name, DnsClient.QueryType.TXT, cancellationToken: ct).ConfigureAwait(false)).Answers.TxtRecords().Select(t => string.Concat(t.Text)).FirstOrDefault(),
                Path.Combine(sp.GetRequiredService<IProfilePaths>().DataDir, "avatars"));
        });
        services.Replace(ServiceDescriptor.Singleton<IAvatarService>(sp => sp.GetRequiredService<AvatarService>()));
        AddReaderMenus(services);
#if AUTOMATION
        services.AddSingleton<Barbican.Automation.IAutomationCommand, Barbican.App.MailBody.Automation.RxBannersCommand>();
        services.AddSingleton<Barbican.Automation.IAutomationCommand, Barbican.App.MailBody.Automation.RxSaveCommand>();
#endif
#if AUTOMATION_WRITES
        services.AddSingleton<Barbican.App.MailBody.Automation.WriteTestGuard>();
        services.AddSingleton<Barbican.Automation.IAutomationCommand, Barbican.App.MailBody.Automation.RxImportCommand>();
        services.AddSingleton<Barbican.Automation.IAutomationCommand, Barbican.App.MailBody.Automation.RxFindCommand>();
        services.AddSingleton<Barbican.Automation.IAutomationCommand, Barbican.App.MailBody.Automation.RxDestroyCommand>();
#endif
        return services;
    }

    // the reader's More menu (MENU-READER), an attachment chip's menu (MENU-ATT) and the commands behind them
    private static void AddReaderMenus(IServiceCollection services)
    {
        services.TryAddSingleton<Barbican.Presentation.Common.IClipboardService, SystemClipboard>();
        services.AddSingleton<Barbican.App.Views.Dialogs.IDialogContent, Barbican.App.Views.Dialogs.SourceDialogContent>();
        services.AddSingleton<Barbican.App.Views.Dialogs.IDialogContent, Barbican.App.Views.Dialogs.AttachmentPreviewDialogContent>();
        services.TryAddSingleton(sp => new Barbican.App.Views.Reader.ReaderMenuOpener(sp.GetRequiredService<Barbican.Presentation.Menus.IContextMenuService>(), sp.GetRequiredService<ReaderViewModel>(), sp.GetRequiredService<IActiveMail>()));
        services.AddSingleton(sp => new Barbican.Presentation.Reader.ReaderCommands(
            sp.GetRequiredService<AttachmentSaver>(),
            () => sp.GetRequiredService<ReaderViewModel>().Current.Body,
            sp.GetRequiredService<Barbican.Presentation.Common.IClipboardService>(),
            sp.GetRequiredService<Barbican.Presentation.Dialogs.IDialogService>(),
            sp.GetService<Barbican.Presentation.Actions.IToastService>()));
        foreach (var i in Enumerable.Range(0, Barbican.Presentation.Reader.ReaderCommands.Count))
        {
            var index = i;
            services.AddSingleton<Barbican.Presentation.Commands.IAppCommand>(sp => sp.GetRequiredService<Barbican.Presentation.Reader.ReaderCommands>().Create()[index]);
        }

        foreach (var i in Enumerable.Range(0, 2))
        {
            var index = i;
            services.AddSingleton<Barbican.Presentation.Menus.IMenuProvider>(sp =>
            {
                var directory = sp.GetRequiredService<Barbican.Presentation.Actions.IMailboxDirectory>();
                var reader = sp.GetRequiredService<ReaderViewModel>();
                return Barbican.Presentation.Reader.ReaderMenus.Providers(() => reader.Current.Body, _ => directory.Tree.All.Any(m => m.Role == Barbican.Core.Mail.Model.MailboxRole.Junk))[index];
            });
        }
    }
}
