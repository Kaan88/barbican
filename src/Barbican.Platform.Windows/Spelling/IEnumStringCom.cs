// Mirrors: Windows objidl.h IEnumString. Source-generated COM (GeneratedComInterface).

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Barbican.Platform.Windows.Spelling;

/// <summary>IEnumString {00000101-0000-0000-C000-000000000046}; each string is CoTaskMem memory the caller frees.</summary>
[GeneratedComInterface]
[Guid("00000101-0000-0000-C000-000000000046")]
internal unsafe partial interface IEnumStringCom
{
    [PreserveSig]
    int Next(uint count, nint* items, uint* fetched);
}
