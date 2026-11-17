// Mirrors: Rust mailto.rs write_registration :250 (reg add) and unregister :296 (reg delete). The only door to the registry for the default-mail-app registration, so a test never touches it (CLAUDE.md: no OS changes without the user's OK). Owner composer-ui.

namespace Barbican.Platform.Mailto;

/// <summary>Writes registry values under HKCU; keys are spelled as <c>DefaultMailApp.WindowsPlan</c> spells them (<c>HKCU\Software\...</c>).</summary>
public interface IRegistryWriter
{
    /// <summary>Sets a string value (name empty: the default value of the key), creating the key.</summary>
    void SetString(string key, string name, string value);

    /// <summary>Deletes a key and everything under it; a key that is gone is fine.</summary>
    void DeleteKey(string key);

    /// <summary>Deletes one value; a value that is gone is fine.</summary>
    void DeleteValue(string key, string name);
}
