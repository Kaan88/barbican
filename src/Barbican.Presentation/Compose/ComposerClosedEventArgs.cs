// Owner composer-ui. See ComposerViewModel.Closed.

namespace Barbican.Presentation.Compose;

/// <summary>How a composer ended: the draft left on the server (null when none) and whether the message was sent or scheduled.</summary>
public sealed class ComposerClosedEventArgs(string? draftId, bool sent) : EventArgs
{
    public string? DraftId { get; } = draftId;

    public bool Sent { get; } = sent;
}
