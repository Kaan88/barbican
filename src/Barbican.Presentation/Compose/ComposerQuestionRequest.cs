// Mirrors: Bulwark ui/dialog.tsx (the one dialog shell) holding email-composer.tsx close dialog :6854-6879, send-warnings-dialog.tsx and external-recipient-confirm.tsx; CP-07. Owner composer-ui.

using Barbican.Presentation.Dialogs;

namespace Barbican.Presentation.Compose;

/// <summary>A question of the composer (leave, discard, send warnings, external recipients) drawn in the dialog host; the answer is the id of the chosen <see cref="DialogChoice"/>, null when it is dismissed.</summary>
public sealed record ComposerQuestionRequest(ComposerDialog Question) : DialogRequest<string>((Question ?? throw new ArgumentNullException(nameof(Question))).Title);
