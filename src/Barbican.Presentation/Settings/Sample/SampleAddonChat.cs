// Mirrors: M6 plan section 3.5 (D-M6-00): an in-memory fake of an add-on chat that behaves like a real one where the M5 sample stores did not: its calls complete off the caller's thread, and its Changed is raised from a pool thread, so a view built on it meets the same threading a real add-on has (M5 found two such bugs, the real store loading on the UI thread and raising from the pool).
// Used by view-model tests and by sample-data runs (BARBICAN_SAMPLE=1); no real add-on uses it.

using Barbican.Plugins;

namespace Barbican.Presentation.Settings.Sample;

/// <summary>An <see cref="IPluginChat"/> that answers with an echo and keeps the turns in memory.</summary>
public sealed class SampleAddonChat : IPluginChat
{
    private readonly Lock _lock = new();
    private readonly List<PluginChatTurn> _turns = [];

    public event EventHandler? Changed;

    public string Title => "Assistant";

    public string InputPlaceholder => "Ask about your mail";

    public string EmptyHint => "Ask a question. Select a message in your list first to ask about it.";

    public Task InitializeAsync(string accountKey, CancellationToken ct) => Task.CompletedTask;

    public IReadOnlyList<PluginChatTurn> Turns(string accountKey)
    {
        lock (_lock)
        {
            return [.. _turns];
        }
    }

    public async Task<PluginChatReply> SendAsync(string accountKey, string text, string? openMessageId, CancellationToken ct)
    {
        var answer = await SampleThreading.OffThreadAsync(() =>
        {
            lock (_lock)
            {
                _turns.Add(new PluginChatTurn(true, text));
                var reply = "Sample answer to: " + text;
                _turns.Add(new PluginChatTurn(false, reply));
                return reply;
            }
        }).ConfigureAwait(false);
        SampleThreading.RaiseOnPool(() => Changed?.Invoke(this, EventArgs.Empty));
        return PluginChatReply.Answer(answer);
    }

    public async Task ClearAsync(string accountKey, CancellationToken ct)
    {
        await SampleThreading.OffThreadAsync(() =>
        {
            lock (_lock)
            {
                _turns.Clear();
            }
        }).ConfigureAwait(false);
        SampleThreading.RaiseOnPool(() => Changed?.Invoke(this, EventArgs.Empty));
    }
}
