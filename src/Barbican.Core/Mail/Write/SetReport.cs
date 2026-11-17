// Mirrors: RFC 8620 section 5.3 (/set response: updated, destroyed, created, notUpdated, notDestroyed, notCreated); M2 plan section 4.3. Contract from stage F of M2; owner actions.

using Barbican.Core.Jmap.Errors;

namespace Barbican.Core.Mail.Write;

/// <summary>What a write changed and what the server refused.</summary>
public sealed record SetReport(
    IReadOnlyList<string> Updated,
    IReadOnlyList<string> Destroyed,
    IReadOnlyList<string> Created,
    IReadOnlyList<SetError> Refused)
{
    /// <summary>
    /// Ids the write never got to or whose request failed: set only on the partial report that comes with a failure (<c>Result.FailPartial</c>) when a
    /// chunked write stopped part way. Ids in <see cref="Updated"/> / <see cref="Destroyed"/> were applied on the server.
    /// </summary>
    public IReadOnlyList<string> NotSent { get; init; } = [];

    /// <summary>An empty report (nothing asked, nothing changed).</summary>
    public static SetReport Empty { get; } = new([], [], [], []);

    /// <summary>True when the server refused nothing.</summary>
    public bool AllAccepted => Refused.Count == 0;
}
