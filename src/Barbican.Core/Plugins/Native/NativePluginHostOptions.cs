// Mirrors: Rust plugins.rs:59 dir (the plugins folder in the config dir), main.rs:455 disabledPlugins; M2 plan 6.5 (the user folder and the bundled
// folder, enable/disable in prefs). Owner unified (M2 wave 2).

using Barbican.Core.Plugins.Hooks;

namespace Barbican.Core.Plugins.Native;

/// <summary>Where the host looks for plugins and what it wires them to.</summary>
public sealed record NativePluginHostOptions
{
    /// <summary>The bundled plugins (<c>&lt;app&gt;/plugins</c>), one subfolder each; loaded first.</summary>
    public string? BundledRoot { get; init; }

    /// <summary>The user's plugins (<c>&lt;config&gt;/plugins</c>), one subfolder each; an id already loaded from the bundled folder is skipped.</summary>
    public string? UserRoot { get; init; }

    /// <summary>Where each plugin gets its own data folder (<c>&lt;data&gt;/plugins/&lt;id&gt;</c>).</summary>
    public string? DataRoot { get; init; }

    /// <summary>Plugin ids the user turned off (prefs disabledPlugins): listed, never initialized or called.</summary>
    public IReadOnlySet<string> Disabled { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>The app's hook bus; plugins that subscribe to catalogue hooks register here. Null: no catalogue hooks (tests of email.details).</summary>
    public HookBus? Bus { get; init; }

    /// <summary>The budget of one email.details call (the Rust hook had none; Bulwark's default 5 s).</summary>
    public TimeSpan DetailsBudget { get; init; } = HookPolicies.Default.Budget;

    /// <summary>The clock of the budgets (tests use a virtual clock).</summary>
    public TimeProvider Time { get; init; } = TimeProvider.System;
}
