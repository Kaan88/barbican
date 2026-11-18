// Mirrors: Rust shortcuts.rs Ctx.editor (the calendar's event editor is open), Bulwark event-modal.tsx; B-KEY-017. What the key tables need to know of the calendar's card layer; implemented by the card host. Additive contract of cal-ui.

namespace Barbican.Presentation.Calendar.Commands;

/// <summary>The state of the calendar that the keys depend on.</summary>
public interface ICalendarKeyContext
{
    /// <summary>A card (an editor or a dialog) is on screen: the single keys are off, Ctrl+Enter saves and Esc closes.</summary>
    bool EditorOpen { get; }
}
