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
