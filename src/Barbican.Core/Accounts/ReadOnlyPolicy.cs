// Mirrors: PORT-PLAN.md §4.6 (the read-only account: every method outside the read allow-list is refused; writes only on the test account),
// M1-plan §1 rule 3 ("M1 is a read-only client"), §6.3 (a per-account read-only flag). The real addresses never appear in the repository.
//
// M2 hotfix (HAND TEST 2, ADR 0008 amendment 2): the read-only rules exist to stop AGENTS from writing to the user's accounts; they never
// restrict the user's own use of the app. So:
// - In the user's own profiles (every profile that is not an agent profile: "dev", the default when BARBICAN_PROFILE is unset, and any other
//   name) every account is writable and a stored read-only flag counts for nothing (Unrestricted). M1/M2 counted "dev" and the empty profile
//   as agent profiles and opened accounts there only through BARBICAN_WRITABLE_ACCOUNTS, which nothing set, so the user's own portable zip
//   was read-only on every account; that variable is gone.
// - The agent rules below apply only to the agent profiles (ProfilePaths.IsAgentProfile: auto-*, smoke, smoke-*, xrun-*, agent-*). Every
//   agent-launched run uses one: an AUTOMATION build or a --smoke run refuses any other profile (HiddenWindowOptions), tools/auto-run.ps1
//   refuses a -ProfileName that is not one, and tools/linux-remote.sh always sets xrun-<name>.
// - A stored read-only flag is honoured only there: only the agent import (RustCredentialImport, agent profiles only) writes one.
//
// The agent rules (unchanged from Fable review 1 F-12 and review 2 R2-14 / M2-Q2, fail closed): an account of an agent profile is writable
// only when its login is on the allow-list in the file named by BARBICAN_WRITABLE_ACCOUNTS_FILE, which must sit in a ".local" folder (the
// gitignored folder of the checkout that holds the test account), and only while a second ".local" list (BARBICAN_READONLY_ACCOUNTS_FILE:
// the logins that are never written) is present and readable, and never for a login on that second list. Without both lists every account
// of an agent profile is read-only, flagged or not. With both, the allow-list also clears the agent import's stored flag for the logins it
// names (R2-14).
// The same two lists guard the write-test commands of the AUTOMATION_WRITES build (TestWriteRefusal, read again for every command).

using Barbican.Core.Storage;

namespace Barbican.Core.Accounts;

/// <summary>Decides which accounts may be written to.</summary>
public sealed class ReadOnlyPolicy
{
    /// <summary>The allow-list file of the agents' profiles: one login per line, <c>#</c> starts a comment.</summary>
    public const string FileVariable = "BARBICAN_WRITABLE_ACCOUNTS_FILE";

    /// <summary>The second list of the agents' profiles: logins that are never written (R2-14, M2-Q2). Same format and folder rule.</summary>
    public const string ReadOnlyFileVariable = "BARBICAN_READONLY_ACCOUNTS_FILE";

    /// <summary>The folder the allow-list file must sit in.</summary>
    public const string LocalFolder = ".local";

    private readonly HashSet<string> _writable;
    private readonly HashSet<string> _neverWritable;

    /// <summary>A policy that writes only to <paramref name="writable"/> (the agent rules without the stored-flag exception).</summary>
    public ReadOnlyPolicy(IEnumerable<string> writable)
        : this(writable, [], mayClearStoredFlag: false, unrestricted: false)
    {
    }

    private ReadOnlyPolicy(IEnumerable<string> writable, IEnumerable<string> neverWritable, bool mayClearStoredFlag, bool unrestricted)
    {
        ArgumentNullException.ThrowIfNull(writable);
        ArgumentNullException.ThrowIfNull(neverWritable);
        _writable = new HashSet<string>(writable.Select(w => w.Trim()).Where(w => w.Length > 0), StringComparer.OrdinalIgnoreCase);
        _neverWritable = new HashSet<string>(neverWritable.Select(w => w.Trim()).Where(w => w.Length > 0), StringComparer.OrdinalIgnoreCase);
        MayClearStoredFlag = mayClearStoredFlag;
        IsUnrestricted = unrestricted;
    }

    /// <summary>Every account read-only (an agent profile without both lists).</summary>
    public static ReadOnlyPolicy AllReadOnly { get; } = new([]);

    /// <summary>Every account writable, stored flags ignored: the user's own profiles.</summary>
    public static ReadOnlyPolicy Unrestricted { get; } = new([], [], mayClearStoredFlag: false, unrestricted: true);

    /// <summary>True for the user's own profiles: nothing is read-only.</summary>
    public bool IsUnrestricted { get; }

    /// <summary>How many logins the agent lists make writable (0: every account is read-only); not used for an unrestricted policy.</summary>
    public int WritableCount => _writable.Count(w => !_neverWritable.Contains(w));

    /// <summary>Whether the allow-list may clear a stored read-only flag (agent profiles with a readable read-only list, R2-14).</summary>
    public bool MayClearStoredFlag { get; }

    /// <summary>The policy of this process.</summary>
    public static ReadOnlyPolicy FromEnvironment() =>
        For(ProfilePaths.FromEnvironment().Profile, Environment.GetEnvironmentVariable, ReadAllowList);

    /// <summary>
    /// The policy of <paramref name="profile"/>: unrestricted in the user's own profiles; in an agent profile only the logins of the two
    /// <c>.local</c> lists (read through <paramref name="readFile"/>, which returns null when the file cannot be read).
    /// </summary>
    public static ReadOnlyPolicy For(string profile, Func<string, string?> env, Func<string, string?> readFile)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(readFile);
        if (!IsAgentProfile(profile))
        {
            return Unrestricted;
        }

        // R2-14, M2-Q2 ("a missing read-only list means nothing is writable"): both lists must be there, else every account is read-only;
        // a login on the read-only list is never writable
        if (LocalList(env(FileVariable), readFile) is not { } writable || LocalList(env(ReadOnlyFileVariable), readFile) is not { } never)
        {
            return AllReadOnly;
        }

        return new(writable, never, mayClearStoredFlag: true, unrestricted: false);
    }

    /// <summary>The profiles only agents run in (auto-*, smoke, xrun-*, agent-*; <see cref="ProfilePaths.IsAgentProfile"/>), where writes fail closed.</summary>
    public static bool IsAgentProfile(string? profile) => ProfilePaths.IsAgentProfile(profile);

    /// <summary>True when writes to <paramref name="user"/> must be refused.</summary>
    public bool IsReadOnly(string user, bool storedFlag)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (IsUnrestricted)
        {
            // the user's own profile: every account is the user's to write, a stored flag included (only the agent import writes one)
            return false;
        }

        var login = user.Trim();
        if (_neverWritable.Contains(login) || !_writable.Contains(login))
        {
            return true;
        }

        // a stored flag (the agent import sets one on every account) is cleared only by the allow-list of an agent profile whose read-only
        // list is present (R2-14)
        return storedFlag && !MayClearStoredFlag;
    }

    /// <summary>
    /// Why the write-test commands of the AUTOMATION_WRITES build must refuse to write to <paramref name="login"/>, or null when they may. Both
    /// <c>.local</c> lists are read again on every call: the login must be on the writable list, and the read-only list must be present and
    /// must not name it. Only an automation profile (auto-*) may run them at all.
    /// </summary>
    public static string? TestWriteRefusal(string? login, string profile, Func<string, string?> env, Func<string, string?> readFile)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(readFile);
        if (string.IsNullOrWhiteSpace(login))
        {
            return "refused: no signed-in account";
        }

        if (!ProfilePaths.IsAutomationProfile(profile))
        {
            return "refused: write tests run only in an automation profile";
        }

        if (LocalList(env(FileVariable), readFile) is not { } writable)
        {
            return "refused: the writable list (.local) is missing";
        }

        if (LocalList(env(ReadOnlyFileVariable), readFile) is not { } never)
        {
            return "refused: the read-only list (.local) is missing";
        }

        var l = login.Trim();
        if (never.Contains(l, StringComparer.OrdinalIgnoreCase))
        {
            return "refused: the account is on the read-only list";
        }

        return writable.Contains(l, StringComparer.OrdinalIgnoreCase) ? null : "refused: the account is not on the writable list";
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

    // The logins of a list file in a .local folder, or null when the path is unset, outside .local or unreadable.
    private static List<string>? LocalList(string? path, Func<string, string?> readFile)
    {
        if (string.IsNullOrWhiteSpace(path) || !InLocalFolder(path) || readFile(path) is not { } text)
        {
            return null;
        }

        return [.. text.Split('\n').Select(l => l.Split('#')[0].Trim()).Where(l => l.Length > 0)];
    }

    private static bool InLocalFolder(string path)
    {
        var dir = Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(path)));
        return string.Equals(dir, LocalFolder, StringComparison.Ordinal);
    }
}
