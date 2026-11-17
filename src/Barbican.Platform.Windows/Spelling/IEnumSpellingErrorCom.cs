// Mirrors: Windows spellcheck.h IEnumSpellingError. Source-generated COM (GeneratedComInterface).

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Barbican.Platform.Windows.Spelling;

/// <summary>IEnumSpellingError {803E3BD4-2828-4410-8290-418D1D73C762}: S_OK with an item, S_FALSE at the end.</summary>
[GeneratedComInterface]
[Guid("803E3BD4-2828-4410-8290-418D1D73C762")]
internal partial interface IEnumSpellingErrorCom
{
    [PreserveSig]
    int Next(out ISpellingErrorCom? error);
}
