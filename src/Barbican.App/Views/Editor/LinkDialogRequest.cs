// Mirrors: Bulwark components/email/link-dialog.tsx:25-129 (Insert link / Edit link: Text and Link, "Remove link" when editing, Save only with an address and no spaces); I-267; CP-06. Owner editor-view.

using Barbican.Presentation.Dialogs;

namespace Barbican.App.Views.Editor;

/// <summary>The link dialog of the editor, drawn by the shared dialog host.</summary>
/// <param name="Text">The selected text, or the link's own text when editing.</param>
/// <param name="Href">The address when editing a link, else empty.</param>
/// <param name="Apply">Save: the text and the trimmed address.</param>
/// <param name="Remove">"Remove link" (only when editing), or null.</param>
public sealed record LinkDialogRequest(string Text, string Href, Action<string, string> Apply, Action? Remove)
    : DialogRequest<bool>(string.IsNullOrEmpty(Href) ? "Insert link" : "Edit link");
