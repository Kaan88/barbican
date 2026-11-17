namespace Barbican.Jmap.Live;

/// <summary>The live suite is off unless asked for exactly (an agent's test run never reaches the server).</summary>
public sealed class LiveGateTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0", false)]
    [InlineData("true", false)]
    [InlineData("1", true)]
    public void OnlyOneEnablesTheSuite(string? value, bool enabled) =>
        Assert.Equal(enabled, LiveGate.Enabled(name => string.Equals(name, LiveGate.Variable, StringComparison.Ordinal) ? value : null));
}
