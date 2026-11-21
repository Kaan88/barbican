// Mirrors: Bulwark components/layout/resize-handle.tsx:166-168 (a focusable separator exposes aria-valuenow / min / max). WP-10 (SH-13).

using Barbican.Presentation.Shell;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;

namespace Barbican.App.Views.Actions;

/// <summary>A focusable separator that exposes its value (WCAG 4.1.2): a range value of the pane width with the pane limits; setting it resizes the pane.</summary>
internal sealed class PaneHandlePeer : FrameworkElementAutomationPeer, IRangeValueProvider
{
    private readonly PaneHandle _handle;

    public PaneHandlePeer(PaneHandle owner)
        : base(owner) => _handle = owner;

    public bool IsReadOnly => false;

    public double LargeChange => PanesViewModel.KeyStep * 4;

    public double Maximum => _handle.Maximum;

    public double Minimum => _handle.Minimum;

    public double SmallChange => PanesViewModel.KeyStep;

    public double Value => _handle.CurrentWidth;

    public void SetValue(double value) => _handle.ApplyWidth(value);

    protected override string GetClassNameCore() => nameof(PaneHandle);

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Separator;

    protected override object? GetPatternCore(PatternInterface patternInterface) => patternInterface == PatternInterface.RangeValue ? this : base.GetPatternCore(patternInterface);
}
