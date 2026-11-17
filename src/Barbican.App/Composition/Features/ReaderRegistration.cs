// Mirrors: M2 plan section 3.3. Owner reader: IGuardedHttp, IAvatarService, IVerificationCodeFinder, IAttachmentSaver, ITrustedSenders, IReaderBannerSource (several), MENU-READER and MENU-ATT providers,
// the attachment and reader commands, the write-test guard.

using Barbican.Core.Mail.AuthResults;
using Barbican.Core.Net;
using Barbican.Core.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.App.Composition.Features;

/// <summary>Reader depth: unsubscribe, receipts, invitations, trust, source, attachments, avatars, guarded HTTP (M2 stream reader).</summary>
public static class ReaderRegistration
{
    public static IServiceCollection AddBarbicanReaderDepth(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IGuardedHttp, GuardedHttp>();
        services.AddSingleton(sp => new AuthServPins(sp.GetRequiredService<IProfilePaths>().DataDir));
#if AUTOMATION_WRITES
        services.AddSingleton<Barbican.App.MailBody.Automation.WriteTestGuard>();
        services.AddSingleton<Barbican.Automation.IAutomationCommand, Barbican.App.MailBody.Automation.RxImportCommand>();
        services.AddSingleton<Barbican.Automation.IAutomationCommand, Barbican.App.MailBody.Automation.RxFindCommand>();
        services.AddSingleton<Barbican.Automation.IAutomationCommand, Barbican.App.MailBody.Automation.RxDestroyCommand>();
#endif
        return services;
    }
}
