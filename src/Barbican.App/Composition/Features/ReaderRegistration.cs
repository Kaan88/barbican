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

        AddPhishingReport(services);
        // the banner stack, in this order above the body (the reported-message banner is registered first, in AddPhishingReport)
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
#if WINDOWS
        services.AddSingleton<Barbican.Automation.IAutomationCommand, Barbican.App.MailBody.Automation.FilterWriteCommand>();
#endif
#endif
        return services;
    }

    // "Report phishing" (G-050): the setup from the organisation policy, the report over the host raw submission, the question and the move to Junk
    private static void AddPhishingReport(IServiceCollection services)
    {
        services.AddSingleton<Barbican.Presentation.Reader.IPhishingReportSetups>(sp => new Barbican.App.Views.Reader.OrganizationPhishingReportSetups(
            sp.GetRequiredService<Barbican.Core.OrganizationPolicy.OrganizationPolicyService>(), sp.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton<Barbican.Core.Compose.RawMessageSender>();
        services.AddSingleton<Barbican.Core.Mail.Safety.IPhishingReporter>(sp => new Barbican.Core.Mail.Safety.PhishingReporter(
            sp.GetRequiredService<IAccountManager>(), sp.GetRequiredService<Barbican.Core.Compose.IIdentityService>(),
            sp.GetRequiredService<Barbican.Core.Compose.RawMessageSender>(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(sp => new Barbican.Presentation.Reader.PhishingReportFlow(
            sp.GetRequiredService<Barbican.Presentation.Reader.IPhishingReportSetups>(), sp.GetRequiredService<Barbican.Core.Mail.Safety.IPhishingReporter>(),
            sp.GetRequiredService<Barbican.Presentation.Actions.MailActionService>(), sp.GetRequiredService<Barbican.Presentation.Dialogs.IDialogService>(),
            sp.GetRequiredService<Barbican.Presentation.Actions.IMailboxDirectory>(), sp.GetRequiredService<Barbican.Presentation.Actions.IToastService>()));
        services.AddSingleton<IReaderBannerSource, PhishingMarkBannerSource>();
        services.AddSingleton<Barbican.Presentation.Commands.IAppCommand>(sp =>
        {
            var flow = sp.GetRequiredService<Barbican.Presentation.Reader.PhishingReportFlow>();
            return new Barbican.Presentation.Commands.DelegateAppCommand(
                Barbican.Presentation.Commands.CommandIds.ReportPhishing,
                c => flow.CanReport(c.Targets, c.Mailbox?.Role),
                (c, ct) => flow.RunAsync(c.Targets, c.Mailbox?.Role, ct));
        });
    }

    // the reader's More menu (MENU-READER), an attachment chip's menu (MENU-ATT) and the commands behind them
    private static void AddReaderMenus(IServiceCollection services)
    {
        services.TryAddSingleton<Barbican.Presentation.Common.IClipboardService, SystemClipboard>();
        services.AddSingleton<Barbican.App.Views.Dialogs.IDialogContent, Barbican.App.Views.Dialogs.SourceDialogContent>();
        services.AddSingleton<Barbican.App.Views.Dialogs.IDialogContent, Barbican.App.Views.Dialogs.AttachmentPreviewDialogContent>();
        services.AddSingleton<Barbican.App.Views.Dialogs.IDialogContent, Barbican.App.Views.Dialogs.RenameAttachmentDialogContent>();
        services.TryAddSingleton<Barbican.App.Views.Reader.ReaderMenuOpener>();
        services.AddSingleton(sp => new Barbican.Presentation.Reader.ReaderCommands(
            sp.GetRequiredService<IAttachmentSaver>(),
            () => sp.GetRequiredService<ReaderViewModel>().Current.Body,
            sp.GetRequiredService<Barbican.Presentation.Common.IClipboardService>(),
            sp.GetRequiredService<Barbican.Presentation.Dialogs.IDialogService>(),
            sp.GetService<Barbican.Presentation.Actions.IToastService>(),
            sp.GetRequiredService<IPrefsStore>(),
            () => sp.GetRequiredService<IActiveMail>().AccountKey,
            sp.GetRequiredService<ReaderViewModel>()));
        foreach (var i in Enumerable.Range(0, Barbican.Presentation.Reader.ReaderCommands.Count))
        {
            var index = i;
            services.AddSingleton<Barbican.Presentation.Commands.IAppCommand>(sp => sp.GetRequiredService<Barbican.Presentation.Reader.ReaderCommands>().Create()[index]);
        }

        // RD-08: the menu on selected message text (MENU-SELECTION), Copy and the add-ons' rows; the add-on side exists where the add-on host does (Windows)
#if WINDOWS
        services.AddSingleton<Barbican.App.Plugins.ReaderSelectionAddOns>();
        services.AddSingleton<IReaderSelectionAddOns>(sp => sp.GetRequiredService<Barbican.App.Plugins.ReaderSelectionAddOns>());
#endif
        services.AddSingleton<Barbican.Presentation.Menus.IMenuProvider>(sp => ReaderSelectionMenu.Provider(() => sp.GetService<IReaderSelectionAddOns>()));
        foreach (var i in Enumerable.Range(0, 2))
        {
            var selection = i;
            services.AddSingleton<Barbican.Presentation.Commands.IAppCommand>(sp =>
                ReaderSelectionMenu.Commands(sp.GetRequiredService<Barbican.Presentation.Common.IClipboardService>(), () => sp.GetService<IReaderSelectionAddOns>())[selection]);
        }

        foreach (var i in Enumerable.Range(0, 2))
        {
            var index = i;
            services.AddSingleton<Barbican.Presentation.Menus.IMenuProvider>(sp =>
            {
                var directory = sp.GetRequiredService<Barbican.Presentation.Actions.IMailboxDirectory>();
                var reader = sp.GetRequiredService<ReaderViewModel>();
                return Barbican.Presentation.Reader.ReaderMenus.Providers(() => reader.Current.Body, _ => directory.Tree.All.Any(m => m.Role == Barbican.Core.Mail.Model.MailboxRole.Junk), sp.GetRequiredService<Barbican.Presentation.Menus.IMenuDataSource>())[index];
            });
        }
    }
}
