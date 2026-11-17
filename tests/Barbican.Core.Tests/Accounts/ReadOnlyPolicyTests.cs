// R2-14 and decision M2-Q2 (docs/milestones/M2-plan.md §6.3 step 1, §9): the full policy matrix of ReadOnlyPolicy (profile x writable
// list x read-only list x stored flag) and the guard of the write-test commands (TestWriteRefusal). M2 hotfix: the agent rules hold only in
// the agent profiles (auto-*, smoke, xrun-*, agent-*); the user's own profiles, "dev" included, write to every account.
// Logins are example.org/example.net placeholders.

using Barbican.Core.Accounts;

namespace Barbican.Core.Tests.Accounts;

public sealed class ReadOnlyPolicyTests
{
    private const string Test = "test@example.org";
    private const string Main = "main@example.net";

    private static readonly string Root = Path.Combine(Path.GetTempPath(), "barbican-rop");
    private static readonly string WritablePath = Path.Combine(Root, ".local", "writable-accounts.txt");
    private static readonly string ReadOnlyPath = Path.Combine(Root, ".local", "readonly-accounts.txt");
    private static readonly string Elsewhere = Path.Combine(Root, "other", "readonly-accounts.txt");

    // writable: "test" (names test), "both" (names test and main), "none" (names nobody we ask about), "missing" (unset), "outside" (not in .local)
    // readOnly: "main" (names main), "test" (names test), "missing", "outside"
    private static (Func<string, string?> Env, Func<string, string?> Read) Lists(string writable, string readOnly)
    {
        var files = new Dictionary<string, string?>(StringComparer.Ordinal);
        string? wPath = null;
        string? rPath = null;
        switch (writable)
        {
            case "test":
                wPath = WritablePath;
                files[wPath] = $"# test account\n{Test}  # writes go to itself\n";
                break;
            case "both":
                wPath = WritablePath;
                files[wPath] = $"{Test}\n{Main}\n";
                break;
            case "none":
                wPath = WritablePath;
                files[wPath] = "other@example.com\n";
                break;
            case "outside":
                wPath = Path.Combine(Root, "other", "writable-accounts.txt");
                files[wPath] = Test;
                break;
        }

        switch (readOnly)
        {
            case "main":
                rPath = ReadOnlyPath;
                files[rPath] = Main + "\r\n";
                break;
            case "test":
                rPath = ReadOnlyPath;
                files[rPath] = Test.ToUpperInvariant();
                break;
            case "outside":
                rPath = Elsewhere;
                files[rPath] = Main;
                break;
        }

        string? Env(string name) => name switch
        {
            ReadOnlyPolicy.FileVariable => wPath,
            ReadOnlyPolicy.ReadOnlyFileVariable => rPath,
            "BARBICAN_WRITABLE_ACCOUNTS" => Main, // the M1 variable is gone: inherited, it opens nothing anywhere
            _ => null,
        };

        return (Env, p => files.GetValueOrDefault(p));
    }

    // profile, writable list, read-only list, stored flag, expected IsReadOnly(test), expected IsReadOnly(main)
    public static TheoryData<string, string, string, bool, bool, bool> Matrix => new()
    {
        // agent profiles without a stored flag: both lists must be there (M2-Q2: a missing read-only list means nothing is writable), and the
        // read-only list wins over the writable one
        { "auto-8889", "test", "main", false, false, true },
        { "auto-8889", "test", "missing", false, true, true },
        { "auto-8889", "test", "outside", false, true, true },
        { "auto-8889", "both", "main", false, false, true },
        { "auto-8889", "both", "missing", false, true, true },
        { "smoke", "both", "missing", false, true, true },
        { "xrun-auto-m2", "test", "main", false, false, true },
        { "agent-a", "missing", "missing", false, true, true },
        { "auto-8889", "test", "test", false, true, true },
        { "auto-8889", "none", "main", false, true, true },
        { "auto-8889", "missing", "main", false, true, true },
        { "auto-8889", "outside", "main", false, true, true },

        // R2-14: the stored flag of an import is cleared only with the read-only list present, never for a login on it
        { "auto-8889", "test", "main", true, false, true },
        { "auto-8889", "both", "main", true, false, true },
        { "auto-8889", "test", "missing", true, true, true },
        { "auto-8889", "test", "outside", true, true, true },
        { "auto-8889", "both", "missing", true, true, true },
        { "auto-8889", "test", "test", true, true, true },
        { "auto-8889", "none", "main", true, true, true },
        { "auto-8889", "missing", "main", true, true, true },
        { "smoke", "test", "main", true, false, true },
        { "xrun-smoke", "test", "missing", true, true, true },

        // M2 hotfix: the user's own profiles ("dev", the default of the portable zip, the empty profile and any other name) write to every
        // account; neither the .local lists nor a stored flag restrict them
        { "dev", "both", "missing", false, false, false },
        { "dev", "test", "main", true, false, false },
        { "dev", "missing", "missing", true, false, false },
        { "dev", "both", "test", false, false, false },
        { "", "missing", "missing", true, false, false },
        { "main", "missing", "missing", false, false, false },
        { "main", "test", "main", true, false, false },
        { "work", "missing", "test", false, false, false },
        { "auto", "missing", "missing", true, false, false },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void The_policy_matrix(string profile, string writable, string readOnly, bool stored, bool testReadOnly, bool mainReadOnly)
    {
        var (env, read) = Lists(writable, readOnly);
        var policy = ReadOnlyPolicy.For(profile, env, read);
        Assert.Equal(testReadOnly, policy.IsReadOnly(Test, stored));
        Assert.Equal(mainReadOnly, policy.IsReadOnly(Main, stored));
    }

    [Fact]
    public void Only_an_agent_profile_with_both_lists_may_clear_a_stored_flag()
    {
        Assert.True(ReadOnlyPolicy.For("auto-1", Lists("test", "main").Env, Lists("test", "main").Read).MayClearStoredFlag);
        Assert.False(ReadOnlyPolicy.For("auto-1", Lists("test", "missing").Env, Lists("test", "missing").Read).MayClearStoredFlag);
        Assert.False(ReadOnlyPolicy.For("main", Lists("test", "main").Env, Lists("test", "main").Read).MayClearStoredFlag);
        Assert.False(new ReadOnlyPolicy([Test]).MayClearStoredFlag);
        Assert.True(ReadOnlyPolicy.For("dev", Lists("missing", "missing").Env, Lists("missing", "missing").Read).IsUnrestricted);
        Assert.False(ReadOnlyPolicy.For("auto-1", Lists("test", "main").Env, Lists("test", "main").Read).IsUnrestricted);
        Assert.Equal(0, ReadOnlyPolicy.For("auto-1", Lists("test", "test").Env, Lists("test", "test").Read).WritableCount);
    }

    // profile, writable list, read-only list, login, expected refusal (null: allowed)
    public static TheoryData<string, string, string, string?, string?> Guard => new()
    {
        { "auto-8889", "test", "main", Test, null },
        { "auto-8889", "both", "main", Test, null },
        { "auto-8889", "both", "main", Main, "refused: the account is on the read-only list" },
        { "auto-8889", "test", "main", "other@example.com", "refused: the account is not on the writable list" },
        { "auto-8889", "test", "test", Test, "refused: the account is on the read-only list" },
        { "auto-8889", "test", "missing", Test, "refused: the read-only list (.local) is missing" },
        { "auto-8889", "test", "outside", Test, "refused: the read-only list (.local) is missing" },
        { "auto-8889", "missing", "main", Test, "refused: the writable list (.local) is missing" },
        { "auto-8889", "outside", "main", Test, "refused: the writable list (.local) is missing" },
        { "auto-8889", "test", "main", null, "refused: no signed-in account" },
        { "auto-8889", "test", "main", " ", "refused: no signed-in account" },
        { "dev", "test", "main", Test, "refused: write tests run only in an automation profile" },
        { "main", "test", "main", Test, "refused: write tests run only in an automation profile" },
    };

    [Theory]
    [MemberData(nameof(Guard))]
    public void The_write_test_guard(string profile, string writable, string readOnly, string? login, string? refusal)
    {
        var (env, read) = Lists(writable, readOnly);
        Assert.Equal(refusal, ReadOnlyPolicy.TestWriteRefusal(login, profile, env, read));
    }

    [Fact]
    public void The_guard_reads_the_lists_again_for_every_command()
    {
        var reads = 0;
        var readOnlyList = Main;
        string? Env(string n) => string.Equals(n, ReadOnlyPolicy.FileVariable, StringComparison.Ordinal) ? WritablePath : string.Equals(n, ReadOnlyPolicy.ReadOnlyFileVariable, StringComparison.Ordinal) ? ReadOnlyPath : null;
        string? Read(string p)
        {
            reads++;
            return string.Equals(p, WritablePath, StringComparison.Ordinal) ? Test : readOnlyList;
        }

        Assert.Null(ReadOnlyPolicy.TestWriteRefusal(Test, "auto-1", Env, Read));
        Assert.Equal(2, reads);

        // the read-only list changes between two commands: the second one is refused
        readOnlyList = $"{Main}\n{Test}";
        Assert.NotNull(ReadOnlyPolicy.TestWriteRefusal(Test, "auto-1", Env, Read));
        Assert.Equal(4, reads);
    }
}
