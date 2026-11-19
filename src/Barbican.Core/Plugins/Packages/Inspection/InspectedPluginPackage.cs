// Source: Bulwark lib/extension-package.ts:401-499, 517-569 and lib/plugin-sandbox/bundle-integrity.ts:48-79; immutable snapshots and archive identity are host-added boundaries.
using System.Collections.Immutable;

namespace Barbican.Core.Plugins.Packages.Inspection;

/// <summary>Validated inert JS package data. This carries no grant, signature verdict, or permission to run.</summary>
public sealed record InspectedPluginPackage(
    string Id,
    string Version,
    string EntryPoint,
    string ManifestJson,
    ImmutableArray<string> Permissions,
    ImmutableArray<string> Warnings,
    string WholeArchiveSha256,
    string BundleHash,
    string ApprovalHash,
    ImmutableDictionary<string, ImmutableArray<byte>> Resources);
