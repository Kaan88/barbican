#pragma warning disable CA1720 // Names follow JavaScript typed-array kinds.
namespace Barbican.Core.Plugins.Protocol;
/// <summary>JavaScript typed-array and DataView kinds.</summary>
public enum PluginViewType { DataView, Int8, Uint8, Uint8Clamped, Int16, Uint16, Int32, Uint32, Float16, Float32, Float64, BigInt64, BigUint64 }
