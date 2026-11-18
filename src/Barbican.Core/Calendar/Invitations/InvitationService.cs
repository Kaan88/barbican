// Mirrors: IInvitationService. Stub from the M4 plan section 4.3; owner cal-edit replaces the class (constructor dependencies: IAccountManager, ICalendarWriter, ICalendarStore, IHookBus).

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Calendar.Invitations;

/// <summary>The default <see cref="IInvitationService"/>.</summary>
public sealed class InvitationService : IInvitationService
{
    public Task<Result<ParsedInvitation>> ParseAsync(string accountKey, string blobId, CancellationToken ct) => throw new PortPendingException("InvitationService.ParseAsync");

    public Task<Result<JsonNode?>> StoredAsync(string accountKey, string uid, CancellationToken ct) => throw new PortPendingException("InvitationService.StoredAsync");

    public Task<Result<string>> AnswerAsync(string accountKey, JsonNode invitation, IReadOnlyList<string> mine, string status, CancellationToken ct) => throw new PortPendingException("InvitationService.AnswerAsync");

    public Task<Result<string>> AddToCalendarAsync(string accountKey, JsonNode invitation, CancellationToken ct) => throw new PortPendingException("InvitationService.AddToCalendarAsync");

    public Task<Result<Unit>> ApplyCounterAsync(string accountKey, JsonNode counter, CancellationToken ct) => throw new PortPendingException("InvitationService.ApplyCounterAsync");

    public Task<Result<Unit>> RemoveCancelledAsync(string accountKey, string uid, CancellationToken ct) => throw new PortPendingException("InvitationService.RemoveCancelledAsync");
}
