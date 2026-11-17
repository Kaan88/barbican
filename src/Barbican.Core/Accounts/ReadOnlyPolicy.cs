// Mirrors: PORT-PLAN.md §4.6 (the read-only account: every method outside the read allow-list is refused; writes only on the test account),
// M1-plan §1 rule 3 ("M1 is a read-only client"), §6.3 (a per-account read-only flag). An account is writable only when the environment
// lists its login in BARBICAN_WRITABLE_ACCOUNTS and its stored entry does not carry readOnly: nothing sets that variable in M1, so every
// account is read-only; M2 opens it for the test account only. The real addresses never appear in the repository.

namespace Barbican.Core.Accounts;

/// <summary>Decides which accounts may be written to.</summary>
public sealed class ReadOnlyPolicy
{
    /// <summary>The comma-separated logins that may be written to.</summary>
    public const string Variable = "BARBICAN_WRITABLE_ACCOUNTS";

    private readonly HashSet<string> _writable;

    public ReadOnlyPolicy(IEnumerable<string> writable)
    {
        ArgumentNullException.ThrowIfNull(writable);
        _writable = new HashSet<string>(writable.Select(w => w.Trim()).Where(w => w.Length > 0), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Every account read-only.</summary>
    public static ReadOnlyPolicy AllReadOnly { get; } = new([]);

    /// <summary>The policy of this process.</summary>
    public static ReadOnlyPolicy FromEnvironment() =>
        new((Environment.GetEnvironmentVariable(Variable) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>True when writes to <paramref name="user"/> must be refused.</summary>
    public bool IsReadOnly(string user, bool storedFlag)
    {
        ArgumentNullException.ThrowIfNull(user);
        return storedFlag || !_writable.Contains(user.Trim());
    }
}
