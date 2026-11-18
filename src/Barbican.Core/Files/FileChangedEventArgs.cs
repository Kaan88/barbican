// Contract from the M5 plan section 4.7; owner files-data.

namespace Barbican.Core.Files;

/// <summary>The file store changed: after a load, a /changes delta from push, or a local change.</summary>
public sealed class FileChangedEventArgs(string accountKey, IReadOnlyList<string> keys) : EventArgs
{
    public string AccountKey { get; } = accountKey;

    /// <summary>Keys of the entries added, changed or removed; empty with a full load.</summary>
    public IReadOnlyList<string> Keys { get; } = keys;
}
