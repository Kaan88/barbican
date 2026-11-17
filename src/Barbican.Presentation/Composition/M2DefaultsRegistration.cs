// Mirrors: M2 plan section 3.3 (F registers every seam of section 4 with TryAdd and a behaviour-preserving default; the owner registers the real one with Replace; registration order makes the later feature win).
// Stage F of M2. Seams without a default (IMailWriterFactory, IUndoService, IToastService, IDialogService, IGuardedHttp, ...) are registered by their owner and consumed with GetService until then.

using Barbican.Core.Mail.Lists;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Mail.Lists;
using Barbican.Presentation.Menus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.Presentation.Composition;

/// <summary>The defaults of the M2 seams.</summary>
public static class M2DefaultsRegistration
{
    public static IServiceCollection AddBarbicanM2Defaults(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ICommandRegistry>(sp => new CommandRegistry(sp.GetServices<IAppCommand>()));
        services.TryAddSingleton<IContextMenuService, NoContextMenuService>();
        services.TryAddSingleton<IMailSelection, EmptyMailSelection>();
        services.TryAddSingleton<IListQueryShaper, IdentityListQueryShaper>();
        services.TryAddSingleton<IListSourceResolver, ActiveListSourceResolver>();
        return services;
    }
}
