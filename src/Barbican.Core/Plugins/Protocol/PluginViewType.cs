// Mirrors: Bulwark lib/plugin-sandbox/protocol.ts:115-334 and runtime.tsx:368-407,646-666; the tagged v1 graph is a native transport adaptation.
#pragma warning disable CA1720 // Names follow JavaScript typed-array kinds.
namespace Barbican.Core.Plugins.Protocol;
/// <summary>JavaScript typed-array and DataView kinds.</summary>
public enum PluginViewType { DataView, Int8, Uint8, Uint8Clamped, Int16, Uint16, Int32, Uint32, Float16, Float32, Float64, BigInt64, BigUint64 }
