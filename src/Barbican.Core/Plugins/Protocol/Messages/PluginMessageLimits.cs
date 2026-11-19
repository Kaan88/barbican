// Reference: lib/plugin-sandbox/protocol.ts:115-336. These are local policy limits, not source constants.
namespace Barbican.Core.Plugins.Protocol.Messages;

public sealed record PluginMessageLimits(int MaxStringLength = 262144, int MaxIdLength = 1024, int MaxSequenceLength = 4096, int MaxOperations = 65536, int MaxGraphNodes = 4096, int MaxGraphEdges = 16384, int MaxGraphStringLength = 1048576, int MaxGraphBinaryBytes = 524288);

