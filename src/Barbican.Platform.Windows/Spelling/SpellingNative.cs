// Mirrors: Win32 combaseapi.h (CoInitializeEx, CoCreateInstance, CoTaskMemFree), winnls.h (GetUserDefaultLocaleName); CLSID_SpellCheckerFactory of spellcheck.h.

using System.Runtime.InteropServices;

namespace Barbican.Platform.Windows.Spelling;

/// <summary>The Win32 calls the checker needs.</summary>
internal static unsafe partial class SpellingNative
{
    public const uint ComApartmentThreaded = 2;
    public const uint ClsctxInprocServer = 1;

    /// <summary>CLSID_SpellCheckerFactory.</summary>
    public static readonly Guid SpellCheckerFactoryClass = new("7AB36653-1796-484B-BDFA-E74F1DB7C1DC");

    [LibraryImport("ole32.dll")]
    public static partial int CoInitializeEx(nint reserved, uint flags);

    [LibraryImport("ole32.dll")]
    public static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);

    [LibraryImport("ole32.dll")]
    public static partial void CoTaskMemFree(nint memory);

    [LibraryImport("kernel32.dll", EntryPoint = "GetUserDefaultLocaleName")]
    public static partial int GetUserDefaultLocaleName(char* buffer, int length);
}
