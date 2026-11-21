// Mirrors: Bulwark components/plugins/plugin-dialog-host.tsx:583-621 (the add-on dialog's header options button and its one menu entry).

namespace Barbican.App.Views.Dialogs;

/// <summary>The header options menu of a dialog: the button's accessible name, the single destructive entry's text and what it does.</summary>
public sealed record DialogHeaderMenu(string ButtonName, string ItemText, Func<Task> Invoke);
