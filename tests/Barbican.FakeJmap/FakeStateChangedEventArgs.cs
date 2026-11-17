// The payload of FakeStore.Changed: the changed types and their new states (RFC 8620 §7.1 StateChange.changed of one account). Owner jmap.

namespace Barbican.FakeJmap;

/// <summary>Which types changed, with their new states.</summary>
public sealed class FakeStateChangedEventArgs(IReadOnlyDictionary<string, string> states) : EventArgs
{
    public IReadOnlyDictionary<string, string> States { get; } = states;
}
