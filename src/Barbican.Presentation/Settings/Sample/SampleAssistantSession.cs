// Mirrors: M6 plan section 3.5 (D-M6-00): an in-memory fake of IAssistantSession that behaves like the real one where the M5 sample stores did not: its calls complete off the caller's thread, and its Changed is raised from a pool thread, so a view built on it meets the same threading the real service has (M5 found two such bugs, the real store loading on the UI thread and raising from the pool).
// Frozen in the M6 plan commit; owner integrator. Used by view-model tests and by sample-data runs (BARBICAN_SAMPLE=1); the data stream's real class never uses it.

using Barbican.Core.Common;
using Barbican.Core.Settings.Assistant;

namespace Barbican.Presentation.Settings.Sample;

/// <summary>An <see cref="IAssistantSession"/> that answers with an echo and keeps the history in memory.</summary>
public sealed class SampleAssistantSession : IAssistantSession
{
    private readonly Lock _lock = new();
    private readonly List<AssistantMessage> _history = [];

    public event EventHandler? Changed;

    public IReadOnlyList<AssistantMessage> History(string accountKey)
    {
        lock (_lock)
        {
            return [.. _history];
        }
    }

    public async Task<Result<string>> AskAsync(string accountKey, string question, string? openMessageId, CancellationToken ct)
    {
        var answer = await SampleThreading.OffThreadAsync(() =>
        {
            lock (_lock)
            {
                _history.Add(new AssistantMessage("user", question, null, null));
                var text = "Sample answer to: " + question;
                _history.Add(new AssistantMessage("assistant", text, null, null));
                return text;
            }
        }).ConfigureAwait(false);
        SampleThreading.RaiseOnPool(() => Changed?.Invoke(this, EventArgs.Empty));
        return Result<string>.Ok(answer);
    }

    public async Task<Result<Unit>> ClearAsync(string accountKey, CancellationToken ct)
    {
        await SampleThreading.OffThreadAsync(() =>
        {
            lock (_lock)
            {
                _history.Clear();
            }
        }).ConfigureAwait(false);
        SampleThreading.RaiseOnPool(() => Changed?.Invoke(this, EventArgs.Empty));
        return Result<Unit>.Ok(default);
    }
}
