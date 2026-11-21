// Mirrors: Bulwark lib/plugin-sandbox/open-email.ts:57-60 OpenEmailOutcome ('opened' or 'missing' or an error).
namespace Barbican.Core.Plugins.Api.Navigation;

/// <summary>What opening a message in the reader came to.</summary>
public enum PluginOpenEmailOutcome
{
    /// <summary>The reader shows the message.</summary>
    Opened,

    /// <summary>The app cannot open it (an account the reader does not show, no mail screen, or the active login changed meanwhile).</summary>
    Refused,
}
