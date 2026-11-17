// Mirrors: ADR 0008 decision 1 (the C# app's own entries: Barbican.Next/<profile>/<name>; never the Rust targets "<user>.Barbican");
// PORT-PLAN.md §3.5 (BARBICAN_PROFILE selects a separate set, so agents' runs never see the user's entries).

namespace Barbican.Platform;

/// <summary>Names of the C# app's secret entries.</summary>
public static class SecretTarget
{
    /// <summary>The prefix of every entry.</summary>
    public const string Prefix = "Barbican.Next/";

    /// <summary>The remembered accounts as one JSON array: the layout of the first C# builds, read once and split (SecretAccountVault, Fable F-05).</summary>
    public static readonly SecretName Accounts = new("accounts");

    /// <summary>The last active account as Rust's session entry: written by the first C# builds only, removed by SecretAccountVault.</summary>
    public static readonly SecretName Session = new("session");

    /// <summary>The Stalwart admin API key (entered by the user in Settings, M7).</summary>
    public static readonly SecretName AdminKey = new("admin-key");

    /// <summary>The assistant provider key (M6).</summary>
    public static readonly SecretName AiKey = new("ai-key");

    /// <summary>"Barbican.Next/<paramref name="profile"/>/<c>kind</c>[/<c>account</c>]".</summary>
    public static string Format(string profile, SecretName name)
    {
        ArgumentException.ThrowIfNullOrEmpty(profile);
        if (string.IsNullOrEmpty(name.Kind))
        {
            throw new ArgumentException("A secret needs a kind.", nameof(name));
        }

        if (profile.Contains('/', StringComparison.Ordinal) || name.Kind.Contains('/', StringComparison.Ordinal))
        {
            throw new ArgumentException("Profile and kind must not contain '/'.", nameof(profile));
        }

        return name.Account is { Length: > 0 } account ? $"{Prefix}{profile}/{name.Kind}/{account}" : $"{Prefix}{profile}/{name.Kind}";
    }
}
