// Mirrors: Bulwark components/ui/dialog.tsx:152-160 (an icon beside the title column) and external-recipient-confirm.tsx:222-225. Owner composer-ui.

namespace Barbican.App.Views.Dialogs;

/// <summary>The icon of a dialog header: a stroke icon kind and the palette keys of its colour and its round background.</summary>
public sealed record DialogIcon(string Kind, string ForegroundKey, string BackgroundKey);
