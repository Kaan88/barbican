// Source: Bulwark lib/plugin-sandbox/host-api.ts:427-445; host asynchronous commit outcomes can be uncertain.
using Barbican.Core.Jmap.Errors;
namespace Barbican.Core.Plugins.Persistence;

/// <summary>A commit may have happened; callers must reload before retrying.</summary>
public sealed record PluginStorageUncertainError() : JmapError("Plugin storage outcome is unknown; reload before retrying.");
