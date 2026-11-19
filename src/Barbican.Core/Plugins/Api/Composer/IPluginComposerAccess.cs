// Mirrors: lib/plugin-sandbox/composer-registry.ts:24-37,62-96; host-api-composer.ts:117-143.
using Barbican.Core.Plugins.Runtime;
namespace Barbican.Core.Plugins.Api.Composer;
/// <summary>Host-owned, per-binding exposure of open composer capabilities.</summary>
public interface IPluginComposerAccess
{
    IPluginComposerHandle? Resolve(PluginRuntimeBinding binding, string composerId);
}
