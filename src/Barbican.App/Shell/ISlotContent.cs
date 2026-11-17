// Mirrors: M2 plan section 3.3 (a slot is a ContentPresenter whose content comes from DI through ViewHost; no new in code-behind). Stage F of M2.

namespace Barbican.App.Shell;

/// <summary>The content a stream puts into a named slot of a shared view.</summary>
public interface ISlotContent
{
    /// <summary>A <see cref="SlotNames"/> constant.</summary>
    string Slot { get; }

    /// <summary>Creates the view for the slot (called once when the hosting view is built).</summary>
    UIElement Create();
}
