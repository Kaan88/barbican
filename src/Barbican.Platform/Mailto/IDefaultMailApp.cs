// Mirrors: Rust mailto.rs register_default :237 / unregister :296. What the Settings button of "Make Barbican the default mail app" runs after the user confirmed. Owner composer-ui.

namespace Barbican.Platform.Mailto;

/// <summary>Registers this app as the handler of mailto: links for the current user.</summary>
public interface IDefaultMailApp
{
    /// <summary>Registers <paramref name="exe"/>; the answer is the line to show the user. Throws <see cref="IOException"/> when the OS refuses.</summary>
    string Register(string exe);

    /// <summary>Removes what <see cref="Register"/> wrote.</summary>
    string Unregister();
}
