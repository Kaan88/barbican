using System.Collections.Immutable;

namespace Barbican.App.Plugins.Sandbox;

internal sealed record PluginSandboxBootstrapAsset(string Purpose, ImmutableArray<byte> Content);
