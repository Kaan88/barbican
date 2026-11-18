// Mirrors: see AddressBooks. Contract from the M5 plan section 4.1; owner con-data.

using Barbican.Core.Common;

namespace Barbican.Core.Contacts;

/// <summary>Address book writes (AddressBook/set).</summary>
public interface IAddressBooks
{
    /// <summary>The id of the default book, creating "Contacts" only when a fresh AddressBook/get finds none (no duplicate book, B-CON-003).</summary>
    Task<Result<string>> EnsureDefaultAsync(string accountKey, CancellationToken ct);

    Task<Result<string>> CreateAsync(string accountKey, string name, CancellationToken ct);

    Task<Result<Unit>> RenameAsync(string accountKey, string bookId, string name, CancellationToken ct);

    /// <summary>Destroys a book; <paramref name="removeContacts"/> maps to onDestroyRemoveContents.</summary>
    Task<Result<Unit>> DeleteAsync(string accountKey, string bookId, bool removeContacts, CancellationToken ct);
}
