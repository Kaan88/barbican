// A fake availability service: supported or not, nobody busy. M4 stream cal-ui.

using Barbican.Core.Calendar.Editing;
using Barbican.Core.Common;

namespace Barbican.Presentation.Tests.Calendar.Editors;

internal sealed class FakeAvailability(bool supported) : IAvailabilityService
{
    public bool IsSupported(string accountKey) => supported;

    public Task<Result<IReadOnlyDictionary<string, Result<IReadOnlyList<(long Start, long End, bool Tentative)>>>>> BusyAsync(string accountKey, IReadOnlyList<string> emails, long fromMs, long toMs, CancellationToken ct) =>
        Task.FromResult(Result<IReadOnlyDictionary<string, Result<IReadOnlyList<(long Start, long End, bool Tentative)>>>>.Ok(
            emails.ToDictionary(e => e, _ => Result<IReadOnlyList<(long Start, long End, bool Tentative)>>.Ok([]), StringComparer.Ordinal)));
}
