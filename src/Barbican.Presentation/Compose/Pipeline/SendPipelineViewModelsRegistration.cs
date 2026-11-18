// Mirrors: M3 plan section 4.7 (one DI registration file per feature), section 4.3 (the pipeline view models). Owner send-pipeline. Not called from AppComposition (frozen): the integrator adds AddBarbicanSendPipelineViewModels() after AddBarbicanComposerViewModels().

using Barbican.Presentation.Reader;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.Presentation.Compose.Pipeline;

/// <summary>Registers the send pipeline view models: the send flow, the template picker, the Scheduled view list, banner and actions.</summary>
public static class SendPipelineViewModelsRegistration
{
    public static IServiceCollection AddBarbicanSendPipelineViewModels(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<SendCoordinator>();
        services.TryAddTransient<TemplatePickerViewModel>();
        services.TryAddSingleton<ScheduledListSource>();
        services.TryAddSingleton<ScheduledBannerActions>();
        services.AddSingleton<IBannerActionHandler>(sp => sp.GetRequiredService<ScheduledBannerActions>());
        services.AddSingleton<IReaderBannerSource, ScheduledBannerSource>();
        return services;
    }
}
