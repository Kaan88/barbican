// Mirrors Bulwark components/plugins/plugin-dialog-host.tsx:229-240 (customProps: the props the add-on passed, then onResult, which answers this dialog only, set last so the add-on cannot replace it).
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Dialogs;

/// <summary>Builds the props of the frame a ui.openDialog dialog draws.</summary>
public static class PluginSlotDialogProps
{
    /// <summary>The name of the host function the slot calls with its answer.</summary>
    public const string OnResult = "onResult";

    /// <summary>The add-on's own props with <c>onResult</c> set to the callback marker <paramref name="onResultId"/>.</summary>
    public static PluginValue Build(PluginValue extraProps, string onResultId)
    {
        ArgumentNullException.ThrowIfNull(extraProps);
        ArgumentException.ThrowIfNullOrEmpty(onResultId);
        var builder = new PluginValueBuilder();
        var properties = new List<(string, int)>();
        foreach (var name in extraProps.PropertyNames)
        {
            if (!string.Equals(name, OnResult, StringComparison.Ordinal)) properties.Add((name, builder.Import(extraProps.Property(name))));
        }

        properties.Add((OnResult, PluginHostCallbacks.Marker(builder, onResultId)));
        return builder.Build(builder.Object([.. properties]));
    }
}
