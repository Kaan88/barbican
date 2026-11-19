// Mirrors: Bulwark lib/plugin-types.ts installed/enabled state. The package manager revalidates authority before execution.
using Barbican.Core.Plugins.Permissions;
namespace Barbican.Core.Plugins.Packages;
public sealed record PluginInstallation(PluginPackageDefinition Package, bool Enabled, PluginGrantSnapshot? Grant, string? Error = null);
