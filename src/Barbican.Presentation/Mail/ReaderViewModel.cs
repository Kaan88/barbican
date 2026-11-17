// Mirrors: M1-plan §4.3 (Header, Conversation strip, Attachments, AuthResults, BodyState, Apply(ReaderSnapshot)); spec I-194..I-233. Contract stub from F2; owner mailcore.

using Barbican.Core.Common;

// CA1822: a stub that no stream has implemented yet; Apply gains state when mailcore lands.
#pragma warning disable CA1822

namespace Barbican.Presentation.Mail;

/// <summary>The reader (the mailcore stream adds Header, Conversation, Attachments, AuthResults and BodyState).</summary>
public sealed class ReaderViewModel
{
    /// <summary>Takes over a prepared snapshot.</summary>
    public void Apply(ReaderSnapshot snapshot) => throw new PortPendingException("ReaderViewModel.Apply: mailcore stream");
}
