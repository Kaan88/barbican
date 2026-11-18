// Mirrors: IIcsImporter. Stub from the M4 plan section 4.3; owner cal-edit replaces the class (constructor dependencies: ICalendarWriter, IAccountManager, IHookBus).

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Calendar.Editing;

/// <summary>The default <see cref="IIcsImporter"/>.</summary>
public sealed class IcsImporter : IIcsImporter
{
    public Task<Result<IReadOnlyList<JsonNode>>> ParseAsync(string accountKey, ReadOnlyMemory<byte> ics, CancellationToken ct) => throw new PortPendingException("IcsImporter.ParseAsync");

    public Task<Result<int>> ImportAsync(string accountKey, string calendarId, IReadOnlyList<JsonNode> events, bool keepAlerts, CancellationToken ct) => throw new PortPendingException("IcsImporter.ImportAsync");
}
