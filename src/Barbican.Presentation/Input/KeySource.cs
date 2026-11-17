// Mirrors: M1-plan §4.3 (a key can come from the real KeyDown, the WebView's forwarded keys, or automation). Contract stub from F2; owner mailcore.

namespace Barbican.Presentation.Input;

/// <summary>Where a key chord came from.</summary>
public enum KeySource
{
    Keyboard,
    WebView,
    Automation,
}
