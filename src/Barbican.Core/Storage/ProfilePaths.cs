// Mirrors: Rust platform/mod.rs:20 config_dir, :33 data_dir, :46 xdg_dir (at rust-final); PORT-PLAN.md §3.5 (profiles: BARBICAN_PROFILE=dev
// gives Barbican-dev folders; agents use auto-<port>), §4.4; ADR 0008 (the profile also scopes the secrets). Until M8 the C# app never uses the
// Rust app's own folders: with no BARBICAN_PROFILE it runs as "dev", so a C# run cannot write the Rust profile (CLAUDE.md standing rules).

namespace Barbican.Core.Storage;

/// <summary>The folders of one profile.</summary>
public sealed class ProfilePaths : IProfilePaths
{
    /// <summary>The variable that names the profile.</summary>
    public const string Variable = "BARBICAN_PROFILE";

    /// <summary>The profile of a run without <see cref="Variable"/> (M1-M7: the dev profile; M8 switches release builds to the real one).</summary>
    public const string DefaultProfile = "dev";

    private ProfilePaths(string profile, string configDir, string dataDir)
    {
        Profile = profile;
        ConfigDir = configDir;
        DataDir = dataDir;
    }

    /// <inheritdoc/>
    public string Profile { get; }

    /// <inheritdoc/>
    public string ConfigDir { get; }

    /// <inheritdoc/>
    public string DataDir { get; }

    /// <summary>True for an automation profile (<c>auto-&lt;port&gt;</c>): fresh, read-only, never imports anything.</summary>
    public bool IsAutomation => IsAutomationProfile(Profile);

    /// <summary>The profile of this process (<see cref="Variable"/>, else <see cref="DefaultProfile"/>).</summary>
    public static ProfilePaths FromEnvironment() =>
        Resolve(Environment.GetEnvironmentVariable(Variable), OperatingSystem.IsWindows(), Environment.GetEnvironmentVariable, HomeDir());

    /// <summary>The folders of <paramref name="profile"/> (null or blank: the default) on the given OS, reading variables through <paramref name="env"/>.</summary>
    public static ProfilePaths Resolve(string? profile, bool windows, Func<string, string?> env, string home)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(home);
        var name = string.IsNullOrWhiteSpace(profile) ? DefaultProfile : profile.Trim();
        if (!IsValidName(name))
        {
            throw new ArgumentException($"{Variable} must be 1-40 characters a-z, 0-9 or '-', starting with a letter or digit.", nameof(profile));
        }

        var (config, data) = BaseDirs(windows, env, home);
        var folder = (windows ? "Barbican-" : "barbican-") + name;
        return new ProfilePaths(name, Path.Combine(config, folder), Path.Combine(data, folder));
    }

    /// <summary>A profile under explicit folders (tests).</summary>
    public static ProfilePaths At(string profile, string configDir, string dataDir)
    {
        ArgumentNullException.ThrowIfNull(configDir);
        ArgumentNullException.ThrowIfNull(dataDir);
        return IsValidName(profile) ? new ProfilePaths(profile, configDir, dataDir) : throw new ArgumentException("Bad profile name.", nameof(profile));
    }

    /// <summary>The Rust app's config folder (the read-only source of the dev profile's first-run copy; Rust platform/mod.rs:20).</summary>
    public static string RustConfigDir(bool windows, Func<string, string?> env, string home)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(home);
        return Path.Combine(BaseDirs(windows, env, home).Config, windows ? "Barbican" : "barbican");
    }

    /// <summary>A profile name: lower-case letters, digits and '-', 1-40 characters, not starting with '-'.</summary>
    public static bool IsValidName(string? name) =>
        name is { Length: >= 1 and <= 40 } && name[0] != '-' && name.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    /// <summary>True for <c>auto-&lt;port&gt;</c> profiles (ADR 0007: one per automation run).</summary>
    public static bool IsAutomationProfile(string? profile) =>
        profile is not null && profile.StartsWith("auto-", StringComparison.Ordinal);

    /// <summary>The user's home folder.</summary>
    public static string HomeDir() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static (string Config, string Data) BaseDirs(bool windows, Func<string, string?> env, string home)
    {
        if (windows)
        {
            var appData = env("APPDATA") is { Length: > 0 } a ? a : ".";
            var local = env("LOCALAPPDATA") is { Length: > 0 } l ? l : appData;
            return (appData, local);
        }

        return (Xdg(env, "XDG_CONFIG_HOME", home, ".config"), Xdg(env, "XDG_DATA_HOME", home, Path.Combine(".local", "share")));
    }

    // Rust xdg_dir: the variable when it is an absolute path (the XDG spec ignores relative values), else $HOME/<fallback>
    private static string Xdg(Func<string, string?> env, string variable, string home, string fallback) =>
        env(variable) is { Length: > 0 } v && v.StartsWith('/') ? v : Path.Combine(home, fallback);
}
