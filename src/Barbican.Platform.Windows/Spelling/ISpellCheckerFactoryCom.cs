// Mirrors: Windows spellcheck.h ISpellCheckerFactory. Source-generated COM (GeneratedComInterface), no CsWin32 (as CredentialApi).

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Barbican.Platform.Windows.Spelling;

/// <summary>ISpellCheckerFactory {8E018A9D-2415-4677-BF08-794EA61F94BB}.</summary>
[GeneratedComInterface]
[Guid("8E018A9D-2415-4677-BF08-794EA61F94BB")]
internal partial interface ISpellCheckerFactoryCom
{
    [PreserveSig]
    int SupportedLanguages(out IEnumStringCom? languages);

    [PreserveSig]
    int IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag, [MarshalAs(UnmanagedType.Bool)] out bool supported);

    [PreserveSig]
    int CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string languageTag, out ISpellCheckerCom? checker);
}
