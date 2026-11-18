// Mirrors: M6 plan section 3.4 (counts the composition tests add): the number of commands the srv-ui stream registers (the Folders page keeps MENU-FOLDER; filters, templates and identities rows). Frozen shape, owner srv-ui: the stream sets Count in the commit that registers them.

namespace Barbican.Presentation.Settings.Server;

/// <summary>How many commands the srv-ui registration adds (AppCompositionTests and MenusCompositionTests add it as its own term).</summary>
public static class SrvUiCommands
{
    public const int Count = 0;
}
