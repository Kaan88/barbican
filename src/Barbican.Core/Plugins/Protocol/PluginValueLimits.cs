// Mirrors: Bulwark lib/plugin-sandbox/protocol.ts:115-334 and runtime.tsx:368-407,646-666; the tagged v1 graph is a native transport adaptation.
namespace Barbican.Core.Plugins.Protocol;
/// <summary>Budgets apply to the complete graph and encoded native message. Defaults are conservative library limits; runtimes must supply operation-specific caps, including supported large-file limits.</summary>
public sealed record PluginValueLimits(int MaxWireBytes = 1_048_576, int MaxBinaryBytes = 524_288, int MaxNodes = 4096, int MaxDepth = 64, int MaxStringBytes = 262_144, int MaxSequenceLength = 4096, int MaxBigIntDigits = 4096);
