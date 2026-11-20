// Bulwark lib/plugin-sandbox/bundle-fetch.ts and app/api/plugins/route.ts.
using System.Collections.Immutable;
using Barbican.Core.Plugins.Packages.Inspection;

namespace Barbican.Core.Plugins.Packages.Managed;

/// <summary>Verified content for a managed row and pinned key. Host provenance and grant approval remain separate.</summary>
internal sealed record ManagedPluginArtifact(ImmutableArray<byte> Archive, InspectedPluginPackage Inspection, string SignerSha256);
