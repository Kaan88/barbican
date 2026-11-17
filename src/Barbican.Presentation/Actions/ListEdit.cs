// Mirrors: Rust mail.rs remove_local :901 (rows leave the list at once, the total drops); the undo of a move puts them back (Bulwark stores/email-store.ts rollback).

using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Actions;

/// <summary>What an optimistic removal took out of the list, to put back on a failed write or an undo.</summary>
/// <param name="Removed">Each removed message with the index it had among the loaded messages.</param>
/// <param name="NextId">The message after the first removed one that was not removed too (the reader opens it when the setting says so); null when none.</param>
public sealed record ListEdit(IReadOnlyList<(int Index, EmailSummary Email)> Removed, string? NextId)
{
    /// <summary>Nothing was removed (the messages were not in the loaded part of the list).</summary>
    public bool IsEmpty => Removed.Count == 0;
}
