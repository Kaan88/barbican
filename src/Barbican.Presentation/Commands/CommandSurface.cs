// Mirrors: M2 plan section 4.1 (CommandSurface). Contract from stage F of M2; owner menus.

namespace Barbican.Presentation.Commands;

/// <summary>Where a command was invoked from.</summary>
public enum CommandSurface
{
    Keyboard,
    ContextMenu,
    Palette,
    Toolbar,
    HoverStrip,
    SelectionBar,
    Banner,
    Automation,
}
