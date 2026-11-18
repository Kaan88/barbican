// Mirrors: IContactCardReader. Stub from the M4 plan section 4.3; owner cal-edit replaces the class.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Calendar.Editing;

/// <summary>The default <see cref="IContactCardReader"/>.</summary>
public sealed class ContactCardReader : IContactCardReader
{
    public Task<Result<IReadOnlyList<JsonNode>>> CardsAsync(string accountKey, CancellationToken ct) => throw new PortPendingException("ContactCardReader.CardsAsync");
}
