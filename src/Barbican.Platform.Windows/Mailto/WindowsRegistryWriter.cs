// Mirrors: Rust mailto.rs write_registration (reg add ... /t REG_SZ /f) and unregister (reg delete ... /f). HKCU only: a key outside HKCU\ is refused. Constructed only when the user confirmed the registration. Owner composer-ui.

using Microsoft.Win32;

namespace Barbican.Platform.Windows.Mailto;

/// <summary>The real registry door of the Windows registration.</summary>
public sealed class WindowsRegistryWriter : Barbican.Platform.Mailto.IRegistryWriter
{
    private const string Hive = @"HKCU\";

    public void SetString(string key, string name, string value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Sub(key), writable: true) ?? throw new IOException("Could not write " + key);
        k.SetValue(name, value, RegistryValueKind.String);
    }

    public void DeleteKey(string key) => Registry.CurrentUser.DeleteSubKeyTree(Sub(key), throwOnMissingSubKey: false);

    public void DeleteValue(string key, string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(Sub(key), writable: true);
        k?.DeleteValue(name, throwOnMissingValue: false);
    }

    private static string Sub(string key) =>
        key.StartsWith(Hive, StringComparison.OrdinalIgnoreCase) ? key[Hive.Length..] : throw new ArgumentException("Only HKCU keys are written: " + key, nameof(key));
}
