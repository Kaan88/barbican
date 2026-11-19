#pragma warning disable CA1720 // Names follow JavaScript structured-clone kinds.
namespace Barbican.Core.Plugins.Protocol;
/// <summary>Supported structured-clone values; unsupported host or script objects are rejected.</summary>
public enum PluginValueKind { Undefined, Null, Boolean, Number, String, Object, Array, ArrayBuffer, View, Blob, Date, Map, Set, BigInt, Error }
