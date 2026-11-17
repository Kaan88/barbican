// The spec rows a test covers: [Rfc("J-014", "RFC 8620 3.7")] tags it with the J- row of spec/jmap.csv and the section it checks.
// Owner jmap (local to the Core tests; tests/Shared holds the cross-project attributes).

using Xunit.v3;

namespace Barbican.Core.Tests.Jmap;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class RfcAttribute(string row, string section) : Attribute, ITraitAttribute
{
    public string Row { get; } = row;

    public string Section { get; } = section;

    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() => [new("Spec", Row), new("Rfc", Section)];
}
