// Mirrors: Bulwark lib/keyboard.ts:355 (dialogs block app keys); M2 plan section 6.2 (KeyGate: dialogs and open menus block app keys; I-168). Owner menus.

namespace Barbican.Presentation.Input;

/// <summary>An open menu, dialog, palette or sheet: while one is open it gets every key and no app key runs.</summary>
public interface IModalSurface
{
    /// <summary>The surface is on screen.</summary>
    bool IsOpen { get; }

    /// <summary>Gives the open surface a key; true when it used it.</summary>
    bool HandleKey(KeyChord chord);
}
