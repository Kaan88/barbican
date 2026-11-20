// Mirrors: Bulwark components/email/selection-view.tsx ("Select all N"), components/ui/toast.tsx (an error toast is red). x:Bind helpers of the action views. Owner actions.

using System.Globalization;

namespace Barbican.App.Views.Actions;

/// <summary>Text and brushes the action views bind to.</summary>
public static class ActionFmt
{
    /// <summary>The toast text brush: the palette text, or the red of an error.</summary>
    public static Microsoft.UI.Xaml.Media.Brush ToastText(Barbican.Presentation.Actions.ToastSeverity severity) => Fmt.Palette(severity switch
    {
        Barbican.Presentation.Actions.ToastSeverity.Error => "RoseFg",
        Barbican.Presentation.Actions.ToastSeverity.Warning => "AmberFg",
        Barbican.Presentation.Actions.ToastSeverity.Success => "MintFg",
        _ => "Text",
    });

    /// <summary>The banner button: "Select all N in this folder".</summary>
    public static string SelectAllText(int total, bool threaded) => threaded
        ? string.Create(CultureInfo.InvariantCulture, $"Select all messages in {total} conversations")
        : string.Create(CultureInfo.InvariantCulture, $"Select all {total} in this folder");
}
