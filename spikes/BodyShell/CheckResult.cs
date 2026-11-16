namespace Barbican.Spike.BodyShell;

/// <summary>One yes/no probe (an API works, a page is blocked, a script did not run).</summary>
internal sealed record CheckResult(string Name, bool Pass, string Detail);
