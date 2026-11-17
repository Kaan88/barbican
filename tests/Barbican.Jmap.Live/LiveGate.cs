namespace Barbican.Jmap.Live;

/// <summary>
/// The opt-in switch of the live suite (PORT-PLAN.md §4.6): live tests run only with
/// <c>BARBICAN_LIVE=1</c> and only read. Every live test starts with <see cref="Require"/>.
/// </summary>
public static class LiveGate
{
    public const string Variable = "BARBICAN_LIVE";

    public static bool Enabled(Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return string.Equals(environment(Variable), "1", StringComparison.Ordinal);
    }

    /// <summary>Skips the calling test unless the live suite was asked for.</summary>
    public static void Require()
    {
        if (!Enabled(Environment.GetEnvironmentVariable))
        {
            Assert.Skip($"live suite: set {Variable}=1 (read-only checks against the real server)");
        }
    }
}
