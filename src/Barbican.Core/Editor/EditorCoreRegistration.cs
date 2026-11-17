// Mirrors: M3 plan section 4.2 (DI skeleton). Owner editor-core: IEditorSessionFactory (the real one replaces the stub), ISpellCheckerFactory default.

using Barbican.Core.Editor.Spell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.Core.Editor;

/// <summary>Registers the editor core's services (M3 stream editor-core).</summary>
public static class EditorCoreRegistration
{
    public static IServiceCollection AddBarbicanEditorCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ISpellCheckerFactory, NoSpellCheckerFactory>();
        services.TryAddSingleton<ISpellEngine, NoSpellEngine>();
        services.TryAddSingleton<IEditorSessionFactory, EditorSessionFactory>();
        return services;
    }
}
