// Mirrors: Bulwark components/email/image-url-dialog.tsx:30-125 (a picture from a web address: https only, with a description); I-266; CP-06. Owner editor-view.

using Barbican.Presentation.Dialogs;

namespace Barbican.App.Views.Editor;

/// <summary>The picture-from-address dialog of the editor, drawn by the shared dialog host.</summary>
/// <param name="Insert">Insert: the checked https address and the description.</param>
public sealed record ImageUrlDialogRequest(Action<string, string> Insert) : DialogRequest<bool>("Insert picture");
