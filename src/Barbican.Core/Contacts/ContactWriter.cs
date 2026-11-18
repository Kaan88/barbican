// Mirrors: see IContactWriter. Stub from the M5 plan section 4.3; owner con-data replaces the class (constructor dependencies: IAccountManager, IContactStore, IAddressBooks, IHookBus).

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Contacts;

/// <summary>The default <see cref="IContactWriter"/>.</summary>
public sealed class ContactWriter : IContactWriter
{
    public Task<Result<string>> CreateAsync(string accountKey, string? bookId, JsonObject card, CancellationToken ct) => throw new PortPendingException("ContactWriter.CreateAsync");

    public Task<Result<Unit>> UpdateAsync(string accountKey, string cardId, JsonObject patch, CancellationToken ct) => throw new PortPendingException("ContactWriter.UpdateAsync");

    public Task<Result<Unit>> DeleteAsync(string accountKey, IReadOnlyList<string> cardIds, CancellationToken ct) => throw new PortPendingException("ContactWriter.DeleteAsync");

    public Task<Result<Unit>> MoveAsync(string accountKey, IReadOnlyList<string> cardIds, string bookId, CancellationToken ct) => throw new PortPendingException("ContactWriter.MoveAsync");

    public Task<Result<string>> CreateGroupAsync(string accountKey, string? bookId, string name, IReadOnlyList<string> memberUids, CancellationToken ct) => throw new PortPendingException("ContactWriter.CreateGroupAsync");

    public Task<Result<Unit>> SetMembersAsync(string accountKey, string groupId, IReadOnlyList<string> add, IReadOnlyList<string> remove, CancellationToken ct) => throw new PortPendingException("ContactWriter.SetMembersAsync");

    public Task<Result<Unit>> MergeAsync(string accountKey, ContactMergePlan plan, IReadOnlyList<JsonNode> groups, CancellationToken ct) => throw new PortPendingException("ContactWriter.MergeAsync");

    public Task<Result<int>> ImportAsync(string accountKey, string bookId, IReadOnlyList<JsonNode> cards, CancellationToken ct) => throw new PortPendingException("ContactWriter.ImportAsync");
}
