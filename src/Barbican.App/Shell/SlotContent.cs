// Mirrors: M2 plan section 3.3 (slots). Stage F of M2.

namespace Barbican.App.Shell;

/// <summary>A slot filler made from a factory, so a registration file stays one line per slot.</summary>
public sealed class SlotContent(string slot, Func<UIElement> create) : ISlotContent
{
    /// <inheritdoc />
    public string Slot { get; } = slot;

    /// <inheritdoc />
    public UIElement Create() => create();
}
