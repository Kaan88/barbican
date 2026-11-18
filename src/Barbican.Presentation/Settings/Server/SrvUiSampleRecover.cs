// Mirrors: M6 plan section 3.5 (sample-data runs): an in-memory ISrvUiRecoverSource that completes off the caller like a real call. Owner srv-ui.

using Barbican.Core.Common;
using Barbican.Core.Mail.Archive;

namespace Barbican.Presentation.Settings.Server;

/// <summary>Sample archived items for the sample-data run and the tests.</summary>
public sealed class SrvUiSampleRecover : ISrvUiRecoverSource
{
    private readonly Lock _lock = new();
    private readonly List<ArchivedItem> _items =
    [
        new("a1", "Quarterly report draft", new DateTimeOffset(2026, 11, 14, 9, 30, 0, TimeSpan.Zero)),
        new("a2", "Invoice 2041", new DateTimeOffset(2026, 11, 9, 16, 5, 0, TimeSpan.Zero)),
        new("a3", "Re: Meeting notes", new DateTimeOffset(2026, 10, 30, 8, 0, 0, TimeSpan.Zero)),
    ];

    public bool Available => true;

    public Task<Result<IReadOnlyList<ArchivedItem>>> ListAsync(CancellationToken ct) =>
        Task.Run(() =>
        {
            lock (_lock)
            {
                return Result<IReadOnlyList<ArchivedItem>>.Ok([.. _items]);
            }
        }, ct);

    public Task<Result<Unit>> RestoreAsync(string id, CancellationToken ct) =>
        Task.Run(() =>
        {
            lock (_lock)
            {
                _ = _items.RemoveAll(i => string.Equals(i.Id, id, StringComparison.Ordinal));
            }

            return Result<Unit>.Ok(default);
        }, ct);
}
