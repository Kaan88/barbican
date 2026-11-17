// Mirrors: Rust mailto.rs write_registration :250 (apply windows_plan, then open the Default apps settings so the user confirms), unregister :296 (apply windows_unplan); the plans are Barbican.Core.Mailto.DefaultMailApp.
// Applies the Windows plan through an IRegistryWriter. Built and unit-tested with a fake writer; the Settings button that calls it asks the user first and no agent ever runs it. Owner composer-ui.

using Barbican.Core.Mailto;

namespace Barbican.Platform.Mailto;

/// <summary>Registers or removes Barbican as a mail client for mailto: links (Windows, current user only).</summary>
public sealed class DefaultMailAppRegistrar(IRegistryWriter registry) : IDefaultMailApp
{
    public string Register(string exe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exe);
        foreach (var write in DefaultMailApp.WindowsPlan(exe))
        {
            registry.SetString(write.Key, write.Name, write.Value);
        }

        return "Barbican is registered for mailto: links. Choose it as the email app in the Windows settings.";
    }

    public string Unregister()
    {
        foreach (var (key, name) in DefaultMailApp.WindowsUnplan())
        {
            if (name.Length == 0)
            {
                registry.DeleteKey(key);
            }
            else
            {
                registry.DeleteValue(key, name);
            }
        }

        return "Barbican no longer handles mailto: links.";
    }
}
