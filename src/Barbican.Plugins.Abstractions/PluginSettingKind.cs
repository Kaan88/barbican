// Mirrors: nothing in Bulwark (native add-on host services, API 1 additive; decision D-M6b-08). The kinds of row the host draws for an add-on's settings.

namespace Barbican.Plugins;

/// <summary>What control the host draws for one add-on setting.</summary>
public enum PluginSettingKind
{
    /// <summary>One line of text.</summary>
    Text = 0,

    /// <summary>Several lines of text.</summary>
    MultilineText = 1,

    /// <summary>One of a few labelled values.</summary>
    Choice = 2,

    /// <summary>On or off ("true" / "false").</summary>
    Toggle = 3,

    /// <summary>A secret (an API key): never stored with the settings, kept in the credential store through <see cref="IPluginSecrets"/>.</summary>
    Secret = 4,
}
