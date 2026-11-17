using Xunit.v3;

namespace Barbican.Testing;

/// <summary>
/// Marks a test that is red on purpose: it was written before its feature (M1-plan F2) and fails with
/// <c>PortPending</c> until the owning stream implements the stub. The xUnit traits are <c>Red=expected</c> and
/// <c>RedStream=&lt;stream&gt;</c>. tests/Directory.Build.props excludes <c>Red=expected</c> from the default run, so
/// <c>tools/build.ps1 test</c> stays green; <c>BARBICAN_RED=1</c> runs only the red tests, and
/// <c>python tools/red-check.py</c> proves each fails for the right reason. The stream that makes a test pass removes
/// the attribute in the same commit (a red test that passes is reported by red-check as "green, remove [Red]").
/// Shared by every test project (linked in by tests/Directory.Build.props).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class RedAttribute(string stream) : Attribute, ITraitAttribute
{
    public string Stream { get; } = stream;

    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() => [new("Red", "expected"), new("RedStream", Stream)];
}
