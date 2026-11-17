// Mirrors: Rust msgload.rs:202 mark_read (c.set_keyword($seen); tag rust-final); Bulwark markAsReadDelay. M1 is a read-only client
// (M1-plan §1 rule 3, §6.5): the registered implementation is ReadOnlyMailActions, which can write nothing. M2 supplies the real one. Owner body.

using Barbican.Core.Common;

namespace Barbican.Presentation.Mail.Loading;

/// <summary>The writes the reader makes on its own (marking an opened message read).</summary>
public interface IMailActions
{
    /// <summary>False while the client may not write (M1, the read-only account): nothing is scheduled.</summary>
    bool CanWrite { get; }

    Task<Result<Unit>> MarkReadAsync(string emailId, CancellationToken ct);
}
