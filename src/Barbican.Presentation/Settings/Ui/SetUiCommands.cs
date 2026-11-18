// Mirrors: M6 plan section 3.4 (counts the composition tests add): the number of commands the set-ui stream registers (settings.open-page, settings.search, settings.back, the assistant column commands; MENU-CALSET and MENU-BOOKSET rows). Frozen shape, owner set-ui: the stream sets Count in the commit that registers them.

namespace Barbican.Presentation.Settings.Ui;

/// <summary>How many commands the set-ui registration adds (AppCompositionTests and MenusCompositionTests add it as its own term).</summary>
public static class SetUiCommands
{
    public const int Count = 0;
}
