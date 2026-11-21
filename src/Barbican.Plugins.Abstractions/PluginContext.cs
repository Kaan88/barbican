// Mirrors: Bulwark lib/plugin-types.ts (the context a plugin gets: its storage, api.hooks); M2 plan 3.2. Owner unified (M2 wave 2).
// The host services below are additive (API 1 stays 1): decision D-M6b-08, the bundled assistant is the reference add-on that uses them. A context made outside the host has none (null).

namespace Barbican.Plugins;

/// <summary>What the host gives a plugin when it starts it.</summary>
/// <param name="PluginDirectory">The folder the plugin was loaded from, or an empty string for a package loaded from memory.</param>
/// <param name="DataDirectory">A folder the plugin may write its own data to.</param>
public sealed record PluginContext(string PluginDirectory, string DataDirectory)
{
    /// <summary>Subscribes to catalogue hooks the manifest lists (none outside the host).</summary>
    public IPluginHooks Hooks { get; init; } = NoPluginHooks.Instance;

    /// <summary>The add-on's per-account settings.</summary>
    public IPluginSettings? Settings { get; init; }

    /// <summary>The add-on's secrets in the credential store.</summary>
    public IPluginSecrets? Secrets { get; init; }

    /// <summary>The host's HTTP client for outbound requests: no redirects, no cookies, no shared credentials. The add-on does not dispose it.</summary>
    public HttpClient? Http { get; init; }

    /// <summary>The mail, calendar, contact, filter and settings operations the host runs for the add-on.</summary>
    public IPluginHostTools? Tools { get; init; }

    /// <summary>The app's own confirmation dialog.</summary>
    public IPluginDialogs? Dialogs { get; init; }
}
