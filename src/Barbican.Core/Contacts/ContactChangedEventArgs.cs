// Contract from the M5 plan section 4.2; owner con-data.

namespace Barbican.Core.Contacts;

/// <summary>The contact store changed: after a load, a /changes delta from push, or a local patch.</summary>
public sealed class ContactChangedEventArgs(string accountKey, bool booksChanged, IReadOnlyList<string> cardIds) : EventArgs
{
    public string AccountKey { get; } = accountKey;

    public bool BooksChanged { get; } = booksChanged;

    /// <summary>Ids of the cards that were added, changed or removed; empty with a full load.</summary>
    public IReadOnlyList<string> CardIds { get; } = cardIds;
}
