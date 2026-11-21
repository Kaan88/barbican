// Mirrors: Bulwark lib/plugin-sandbox/assistant/c.batch.ts:34-40,89-111 (restore points taken before a batch; the undo runs once, shared by the app's undo history and the add-on's email.undoBatch).
namespace Barbican.Core.Plugins.Api.MailActions;

/// <summary>What one add-on batch changed, with the action that puts it back once.</summary>
internal sealed class PluginBatchRestore
{
    private readonly Lock _gate = new();
    private Task<bool>? _running;

    /// <summary>Puts the batch back; true when everything went back. Set by the batch that made this restore.</summary>
    internal Func<CancellationToken, Task<bool>> Undo { get; set; } = _ => Task.FromResult(false);

    /// <summary>The id of the entry in the app's undo history, when one was recorded.</summary>
    internal string? EntryId { get; set; }

    /// <summary>Runs the undo once while it runs or after it went through; a failure may be tried again (c.batch.ts:34-40 once).</summary>
    internal Task<bool> RunOnceAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            return _running ??= RunAsync(ct);
        }
    }

    private async Task<bool> RunAsync(CancellationToken ct)
    {
        var ok = false;
        try
        {
            ok = await Undo(ct).ConfigureAwait(false);
            return ok;
        }
        finally
        {
            if (!ok)
            {
                lock (_gate) _running = null;
            }
        }
    }
}
