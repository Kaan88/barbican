#if WINDOWS
// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:1394-1411 downloadFile (the add-on's bytes reach the user's disk). Barbican asks first: the platform save dialog (the same TransferPicks the mail export uses, so a hidden run answers from
// queued picks and never shows one) names the file; the add-on never names a path. The dialog can stay open, so the grant is checked again right before the write (beforeWrite). A dialog that cannot open answers cancelled.
using Barbican.Core.Plugins.Api.Navigation;
using Barbican.Presentation.Common;
using Barbican.Presentation.Unified;

namespace Barbican.App.Plugins;

internal sealed class PluginFileSaver(TransferPicks picks, IDispatcher dispatcher) : IPluginFileSaver
{
    public async Task<PluginSaveOutcome> SaveAsync(string suggestedName, ReadOnlyMemory<byte> content, Func<CancellationToken, ValueTask<bool>> beforeWrite, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(beforeWrite);
        string? path = null;
        // the dialog belongs to the UI thread
        await dispatcher.InvokeAsync(async () => path = await picks.SaveFileAsync(suggestedName, ct).ConfigureAwait(true)).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(path) || !await beforeWrite(ct).ConfigureAwait(false)) return PluginSaveOutcome.Cancelled;
        try
        {
            await File.WriteAllBytesAsync(Path.GetFullPath(path), content, ct).ConfigureAwait(false);
            return PluginSaveOutcome.Saved;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return PluginSaveOutcome.Failed;
        }
    }
}
#endif
