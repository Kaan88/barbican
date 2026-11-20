// Mirrors Bulwark components/email/email-composer.tsx, classification and takeover before standard submission.
namespace Barbican.Core.Compose;

internal sealed record PreparedComposeSend(ComposeDraft Draft, string Html, string Text,
    ComposerClassificationDecision? Classification, RawMessageReceipt? Takeover = null);
