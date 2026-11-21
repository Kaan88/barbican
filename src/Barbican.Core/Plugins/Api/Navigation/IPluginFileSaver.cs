// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:1394-1411 downloadFile (a browser download of bytes the add-on made). Barbican writes to disk, so the USER picks the place in the platform's own save dialog
// (an OS dialog, never an in-app popup); the add-on never names a path. A hidden automation run answers through queued picks, never a dialog.
namespace Barbican.Core.Plugins.Api.Navigation;

/// <summary>Saves a file an add-on made, after asking the user where.</summary>
public interface IPluginFileSaver
{
    /// <summary>
    /// Shows the platform save dialog suggesting <paramref name="suggestedName"/>, then writes <paramref name="content"/> to the chosen file. The dialog can stay open a long time, so
    /// <paramref name="beforeWrite"/> is asked after the pick and right before the write; when it answers false nothing is written and the outcome is <see cref="PluginSaveOutcome.Cancelled"/>.
    /// </summary>
    Task<PluginSaveOutcome> SaveAsync(string suggestedName, ReadOnlyMemory<byte> content, Func<CancellationToken, ValueTask<bool>> beforeWrite, CancellationToken ct);
}
