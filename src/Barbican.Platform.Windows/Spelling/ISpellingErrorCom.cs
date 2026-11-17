// Mirrors: Windows spellcheck.h ISpellingError. Source-generated COM (GeneratedComInterface).

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Barbican.Platform.Windows.Spelling;

/// <summary>ISpellingError {B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3}; corrective action: 0 none, 1 suggestions, 2 replace, 3 delete (a repeated word).</summary>
[GeneratedComInterface]
[Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3")]
internal partial interface ISpellingErrorCom
{
    [PreserveSig]
    int GetStartIndex(out uint start);

    [PreserveSig]
    int GetLength(out uint length);

    [PreserveSig]
    int GetCorrectiveAction(out int action);
}
