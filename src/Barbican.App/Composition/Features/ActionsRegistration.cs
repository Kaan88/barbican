// Mirrors: M2 plan section 3.3 (one Add<Feature>() per stream; stage F creates it, the owner fills it). Owner actions: IMailWriterFactory, IUndoService, IToastService, IMailSelection (Replace), the action
// and list IAppCommands, the toast host IShellOverlay, the selection bar slot and the real IMailActions.

using Barbican.Presentation.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.App.Composition.Features;

/// <summary>Mail and folder actions, undo and toasts, multi-select, drag and drop, panes, tags (M2 stream actions).</summary>
public static class ActionsRegistration
{
    public static IServiceCollection AddBarbicanMailActions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // the replaceable defaults of every M2 seam (TryAdd; the owners Replace them), registered once, here, because this is the first of the five calls
        services.AddBarbicanM2Defaults();
        return services;
    }
}
