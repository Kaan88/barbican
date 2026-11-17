// Mirrors: Windows spellcheck.h ISpellChecker. Source-generated COM (GeneratedComInterface), no CsWin32 (as CredentialApi).

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Barbican.Platform.Windows.Spelling;

/// <summary>ISpellChecker {B6FD0B71-E2BC-4653-8D05-F197E412770B}; only the methods up to Ignore are declared (the vtable order is the header's).</summary>
[GeneratedComInterface]
[Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B")]
internal partial interface ISpellCheckerCom
{
    [PreserveSig]
    int GetLanguageTag(out nint languageTag);

    [PreserveSig]
    int Check([MarshalAs(UnmanagedType.LPWStr)] string text, out IEnumSpellingErrorCom? errors);

    [PreserveSig]
    int Suggest([MarshalAs(UnmanagedType.LPWStr)] string word, out IEnumStringCom? suggestions);

    [PreserveSig]
    int Add([MarshalAs(UnmanagedType.LPWStr)] string word);

    [PreserveSig]
    int Ignore([MarshalAs(UnmanagedType.LPWStr)] string word);
}
