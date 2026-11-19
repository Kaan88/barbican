#pragma warning disable CA1720, MA0048 // Names mirror structured-clone types; closely related protocol types are grouped.
namespace Barbican.Core.Plugins.Protocol;

/// <summary>Supported structured-clone values; unsupported host or script objects are rejected.</summary>
public enum PluginValueKind { Undefined, Null, Boolean, Number, String, Object, Array, ArrayBuffer, View, Blob, Date, Map, Set, BigInt, Error }

/// <summary>JavaScript typed-array and DataView kinds.</summary>
public enum PluginViewType { DataView, Int8, Uint8, Uint8Clamped, Int16, Uint16, Int32, Uint32, Float16, Float32, Float64, BigInt64, BigUint64 }

/// <summary>Budgets apply to the complete graph and encoded native message.</summary>
public sealed record PluginValueLimits(int MaxWireBytes = 1_048_576, int MaxBinaryBytes = 524_288, int MaxNodes = 4096, int MaxDepth = 64, int MaxStringBytes = 262_144, int MaxSequenceLength = 4096, int MaxBigIntDigits = 4096);

public sealed class PluginValueFormatException : FormatException
{
    public PluginValueFormatException(string message) : base(message) { }
    public PluginValueFormatException(string message, Exception innerException) : base(message, innerException) { }
}

internal sealed record PluginNode(PluginValueKind Kind, string? Text = null, bool Flag = false, byte[]? Bytes = null, int[]? Links = null, string[]? Keys = null, PluginViewType View = default, int Offset = 0, int Length = 0);
