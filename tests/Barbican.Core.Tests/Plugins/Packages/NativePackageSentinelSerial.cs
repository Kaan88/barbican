namespace Barbican.Core.Tests.Plugins.Packages;

/// <summary>The process-owned marker directory is shared by sentinel tests, so their tests must not overlap.</summary>
[CollectionDefinition("Native package sentinel serial", DisableParallelization = true)]
public sealed class NativePackageSentinelSerial;
