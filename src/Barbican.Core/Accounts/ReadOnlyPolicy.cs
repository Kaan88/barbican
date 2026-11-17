// Mirrors: PORT-PLAN.md §4.6 (the read-only account: every method outside the read allow-list is refused; writes only on the test account),
// M1-plan §1 rule 3 ("M1 is a read-only client"), §6.3 (a per-account read-only flag). The real addresses never appear in the repository.
// Fable review 1 F-12 (fail closed): in the agents' profiles (dev, auto-*) an account is writable only when its login is on the allow-list in
// the file named by BARBICAN_WRITABLE_ACCOUNTS_FILE, which must sit in a ".local" folder (the gitignored folder of the checkout that holds the
// test account); a missing, unreadable or misplaced file means every account is read-only, and the plain BARBICAN_WRITABLE_ACCOUNTS variable
// counts for nothing there (an inherited variable must not open an account to an agent). In the user's own profiles that variable still
// lists the writable logins (nothing sets it in M1, so every account is read-only). A stored readOnly flag always wins.

using Barbican.Core.Storage;

namespace Barbican.Core.Accounts;

/// <summary>Decides which accounts may be written to.</summary>
public sealed class ReadOnlyPolicy
{
    /// <summary>The comma-separated logins that may be written to (the user's own profiles only).</summary>
    public const string Variable = "BARBICAN_WRITABLE_ACCOUNTS";

    /// <summary>The allow-list file of the agents' profiles: one login per line, <c>#</c> starts a comment.</summary>
    public const string FileVariable = "BARBICAN_WRITABLE_ACCOUNTS_FILE";

    /// <summary>The folder the allow-list file must sit in.</summary>
    public const string LocalFolder = ".local";

    private readonly HashSet<string> _writable;

    public ReadOnlyPolicy(IEnumerable<string> writable)
    {
        ArgumentNullException.ThrowIfNull(writable);
        _writable = new HashSet<string>(writable.Select(w => w.Trim()).Where(w => w.Length > 0), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Every account read-only.</summary>
    public static ReadOnlyPolicy AllReadOnly { get; } = new([]);

    /// <summary>How many logins are writable (0: every account is read-only).</summary>
    public int WritableCount => _writable.Count;

    /// <summary>The policy of this process.</summary>
    public static ReadOnlyPolicy FromEnvironment() =>
        For(ProfilePaths.FromEnvironment().Profile, Environment.GetEnvironmentVariable, ReadAllowList);

    /// <summary>
    /// The policy of <paramref name="profile"/>: the agents' profiles read only the <c>.local</c> allow-list (through
    /// <paramref name="readFile"/>, which returns null when the file cannot be read); other profiles read <see cref="Variable"/>.
    /// </summary>
    public static ReadOnlyPolicy For(string profile, Func<string, string?> env, Func<string, string?> readFile)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(readFile);
        if (!IsAgentProfile(profile))
        {
            return new((env(Variable) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries));
        }

        var path = env(FileVariable);
        if (string.IsNullOrWhiteSpace(path) || !InLocalFolder(path) || readFile(path) is not { } text)
        {
            return AllReadOnly;
        }

        return new(text.Split('\n').Select(l => l.Split('#')[0].Trim()).Where(l => l.Length > 0));
    }

    /// <summary>The profiles agents run in (dev and auto-*), where writes fail closed.</summary>
    public static bool IsAgentProfile(string? profile) =>
        string.IsNullOrEmpty(profile)
        || string.Equals(profile, ProfilePaths.DefaultProfile, StringComparison.Ordinal)
        || ProfilePaths.IsAutomationProfile(profile);

    /// <summary>True when writes to <paramref name="user"/> must be refused.</summary>
    public bool IsReadOnly(string user, bool storedFlag)
    {
        ArgumentNullException.ThrowIfNull(user);
        return storedFlag || !_writable.Contains(user.Trim());
    }

    private static bool InLocalFolder(string path)
    {
        var dir = Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(path)));
        return string.Equals(dir, LocalFolder, StringComparison.Ordinal);
    }

    /// <summary>Reads the allow-list file (at most 64 KiB), or null when it is missing or unreadable.</summary>
    public static string? ReadAllowList(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length <= 64 * 1024 ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
