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

    /// <summary>The status icon of a toast (Bulwark toast.tsx:92-97: check, info, triangle for a warning, circle for an error).</summary>
    public static string ToastIcon(Barbican.Presentation.Actions.ToastSeverity severity) => severity switch
    {
        Barbican.Presentation.Actions.ToastSeverity.Success => "Check",
        Barbican.Presentation.Actions.ToastSeverity.Info => "Info",
        _ => "Alert",
    };

    /// <summary>Only errors and warnings carry colour; success and info stay neutral (toast.tsx:99-107).</summary>
    public static Microsoft.UI.Xaml.Media.Brush ToastIconBrush(Barbican.Presentation.Actions.ToastSeverity severity) => Fmt.Palette(severity switch
    {
        Barbican.Presentation.Actions.ToastSeverity.Error => "RoseFg",
        Barbican.Presentation.Actions.ToastSeverity.Warning => "AmberFg",
        _ => "Dim",
    });

    /// <summary>Visible when <paramref name="hidden"/> is false.</summary>
    public static Microsoft.UI.Xaml.Visibility NotVis(bool hidden) => hidden ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

    /// <summary>The banner button: "Select all N in this folder".</summary>
    public static string SelectAllText(int total, bool threaded) => threaded
        ? string.Create(CultureInfo.InvariantCulture, $"Select all messages in {total} conversations")
        : string.Create(CultureInfo.InvariantCulture, $"Select all {total} in this folder");
}
