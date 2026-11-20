// Bulwark app/api/plugins/route.ts and lib/plugin-sandbox/loader.ts managed package verification.
namespace Barbican.Core.Plugins.Packages.Managed;

/// <summary>A verified bundle and the exact provider authorization that yielded it.</summary>
internal sealed record ManagedPluginCandidate(ManagedPluginArtifact Artifact, Uri Origin, string LocalAccountKey, Func<Func<bool>, bool> TryRunWhileCurrent);
