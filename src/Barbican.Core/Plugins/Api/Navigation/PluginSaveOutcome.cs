// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:1394-1411 downloadFile (the browser saves the blob); Barbican asks the user where.
namespace Barbican.Core.Plugins.Api.Navigation;

/// <summary>What saving an add-on's file came to.</summary>
public enum PluginSaveOutcome
{
    /// <summary>The user chose a place and the file is written.</summary>
    Saved,

    /// <summary>The user closed the save dialog, or none could open: nothing was written.</summary>
    Cancelled,

    /// <summary>The chosen place could not be written.</summary>
    Failed,
}
