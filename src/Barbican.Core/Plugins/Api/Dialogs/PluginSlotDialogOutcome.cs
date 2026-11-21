using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Dialogs;

/// <summary>How a presenter ended a ui.openDialog dialog: with the value the slot passed to its onResult prop, without one (closed by the user), or with an error.</summary>
public sealed record PluginSlotDialogOutcome(PluginErrorCode? Error, PluginValue? Result)
{
    /// <summary>The user closed the dialog (Escape or the close button) before the slot answered.</summary>
    public static PluginSlotDialogOutcome Closed { get; } = new(null, null);

    /// <summary>The dialog could not be shown or was ended by the host.</summary>
    public static PluginSlotDialogOutcome Failure(PluginErrorCode error) => new(error, null);

    /// <summary>The slot called onResult with <paramref name="value"/>.</summary>
    public static PluginSlotDialogOutcome Answered(PluginValue value) => new(null, value);
}
