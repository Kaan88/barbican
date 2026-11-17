namespace Barbican.Automation.Protocol;

/// <summary>
/// Which popup-owning XAML controls the App may use (docs/adr/0007 decision 4, ADR 0005 "M0 review";
/// enforced by tests/Barbican.Automation.Tests/PopupLintTests.cs over src/Barbican.App).
/// A control on <see cref="MustConstrain"/> may be declared in XAML only with
/// <c>ShouldConstrainToRootBounds="True"</c>. A control on <see cref="Denied"/> may not be used at all:
/// it either has no such switch or was not verified in-window by <c>popup-probe</c> on a private desktop
/// (docs/adr/0007 "M1 F1"). Code-created popups are forbidden in the App outside <see cref="CodeAllowed"/>.
/// </summary>
public static class PopupControls
{
    public static IReadOnlyList<string> MustConstrain { get; } = ["Flyout", "MenuFlyout", "CommandBarFlyout", "Popup", "TeachingTip"];

    /// <summary>Elements (and the ToolTipService.ToolTip attribute) the App may not use until verified in-window.</summary>
    public static IReadOnlyList<string> Denied { get; } =
        ["ComboBox", "ToolTip", "ToolTipService.ToolTip", "AutoSuggestBox", "ContentDialog", "CalendarDatePicker", "DatePicker", "TimePicker", "SplitButton", "DropDownButton", "MenuBar", "NumberBox"];

    /// <summary>Files of src/Barbican.App (repo-relative) that may create popups in code; M2's constrained menu host goes here.</summary>
    public static IReadOnlyList<string> CodeAllowed { get; } = [];
}
