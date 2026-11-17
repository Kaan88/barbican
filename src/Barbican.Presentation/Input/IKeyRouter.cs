// Mirrors: spec/behaviours/shortcuts.md; hooks onBeforeShortcut / onAfterShortcut; M1-plan §4.3. Owner mailcore.

namespace Barbican.Presentation.Input;

/// <summary>Routes key chords to app actions (real KeyDown, WebView-forwarded and automation all arrive here).</summary>
public interface IKeyRouter
{
    /// <summary>True when the chord was handled (and so must not reach the focused control).</summary>
    bool Handle(KeyChord chord, KeySource source);
}
