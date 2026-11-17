// Mirrors: M3 plan section 4.4 (DI skeleton). Owner send-pipeline: every service below is replaced by its real implementation in this file (constructor dependencies: IMailWriterFactory, IIdentityService, IHookBus, TimeProvider, IPrefsStore).

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.Core.Compose;

/// <summary>Registers the send pipeline (M3 stream send-pipeline): seeding, drafts, sending, scheduled sends, attachments, identities, templates.</summary>
public static class SendPipelineRegistration
{
    public static IServiceCollection AddBarbicanSendPipeline(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IComposeSeeder, ComposeSeeder>();
        services.TryAddSingleton<IDraftService, DraftService>();
        services.TryAddSingleton<IMailSender, MailSender>();
        services.TryAddSingleton<IScheduledSends, ScheduledSends>();
        services.TryAddSingleton<IAttachmentUploader, AttachmentUploader>();
        services.TryAddSingleton<IIdentityService, IdentityService>();
        services.TryAddSingleton<ITemplateStore, TemplateStore>();
        return services;
    }
}
