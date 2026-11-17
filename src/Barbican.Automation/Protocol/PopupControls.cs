namespace Barbican.Automation.Protocol;

/// <summary>
/// Which popup-owning XAML controls the App may use (docs/adr/0007 decision 4, ADR 0005 "M0 review";
/// enforced by tests/Barbican.Automation.Tests/PopupLintTests.cs over src/Barbican.App).
/// A control on <see cref="MustConstrain"/> may be declared in XAML only with
/// <c>ShouldConstrainToRootBounds="True"</c>. A control on <see cref="Denied"/> may not be used at all:
/// <c>popup-probe</c> on a private desktop (docs/adr/0007 "M1 F1", 2026-11-17, WinAppSDK 2.6.5) saw it
/// open a popup outside the window (CommandBarFlyout even with the switch on; ComboBox, ToolTip,
/// AutoSuggestBox have no switch), or it was not probed. ContentDialog was probed and stays in the
/// window. Code-created popups are forbidden in the App outside <see cref="CodeAllowed"/>.
/// </summary>
public static class PopupControls
{
    public static IReadOnlyList<string> MustConstrain { get; } = ["Flyout", "MenuFlyout", "Popup", "TeachingTip"];

    /// <summary>Elements (and the ToolTipService.ToolTip attribute) the App may not use until verified in-window.</summary>
    public static IReadOnlyList<string> Denied { get; } =
        ["CommandBarFlyout", "ComboBox", "ToolTip", "ToolTipService.ToolTip", "AutoSuggestBox", "CalendarDatePicker", "DatePicker", "TimePicker", "SplitButton", "DropDownButton", "MenuBar", "NumberBox"];

    /// <summary>Files of src/Barbican.App (repo-relative) that may create popups in code; M2's constrained menu host goes here.</summary>
    public static IReadOnlyList<string> CodeAllowed { get; } = [];
}
