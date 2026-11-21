using Barbican.Core.Plugins.Bridge;

namespace Barbican.Core.Plugins.Api.Dialogs;

/// <summary>How a presenter ended a dialog. A cancelled dialog is <see cref="Confirmed"/> false; a failure carries an error.</summary>
public sealed record PluginDialogOutcome(PluginErrorCode? Error, bool Confirmed, IReadOnlyDictionary<string, string>? Values)
{
    /// <summary>The user chose Cancel or pressed Escape.</summary>
    public static PluginDialogOutcome Cancelled { get; } = new(null, false, null);

    /// <summary>The dialog could not be shown or was closed by the host.</summary>
    public static PluginDialogOutcome Failure(PluginErrorCode error) => new(error, false, null);

    /// <summary>The user chose OK, with a prompt's values when there were fields.</summary>
    public static PluginDialogOutcome Accepted(IReadOnlyDictionary<string, string>? values = null) => new(null, true, values);
}
