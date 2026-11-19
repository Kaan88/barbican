// Source: Bulwark lib/extension-package.ts:395-499 (validated manifest); this internal definition is a host-owned representation.
namespace Barbican.Core.Plugins.Packages.Inspection;

internal sealed record ManifestDefinition(string Id, string Version, string EntryPoint,
    Dictionary<string, object?> Fields, List<string> Permissions, List<string> Warnings);
