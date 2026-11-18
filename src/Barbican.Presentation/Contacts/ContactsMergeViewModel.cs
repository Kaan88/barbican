// Mirrors: Rust contact_merge.rs (find_groups, plan), contacts_x.rs merge apply; Bulwark components/contacts/contact-merge-dialog.tsx, lib/contacts/merge.ts; B-CON-017..019; I-362; L-032.
// One set of probable duplicates at a time: the card that is kept and what the others add to it; the write is patch, group moves, then deletes (IContactWriter.MergeAsync). Owner con-ui.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Contacts;

namespace Barbican.Presentation.Contacts;

/// <summary>The merge dialog over the sets of duplicates.</summary>
public sealed class ContactsMergeViewModel : ContactsDialogModel
{
    private readonly IContactsViewModel _contacts;
    private readonly IContactWriter _writer;
    private int _index;

    public ContactsMergeViewModel(IContactsViewModel contacts, IContactWriter writer)
    {
        ArgumentNullException.ThrowIfNull(contacts);
        ArgumentNullException.ThrowIfNull(writer);
        _contacts = contacts;
        _writer = writer;
    }

    /// <summary>The sets of cards that look like one person.</summary>
    public IReadOnlyList<IReadOnlyList<JsonNode>> Sets => _contacts.DuplicateSets;

    public int Index
    {
        get => _index;
        set => SetProperty(ref _index, Math.Clamp(value, 0, Math.Max(0, Sets.Count - 1)));
    }

    public IReadOnlyList<JsonNode> Current => Sets.Count == 0 ? [] : Sets[Math.Min(_index, Sets.Count - 1)];

    /// <summary>The card the merge keeps and the others, or null for an empty set.</summary>
    public ContactMergePlan? Plan()
    {
        try
        {
            return Current.Count == 0 ? null : ContactMerge.Plan(Current);
        }
        catch (PortPendingException)
        {
            return null;
        }
    }

    /// <summary>"Ada Lovelace (ada@example.org)" for each card of the current set.</summary>
    public IReadOnlyList<string> Labels => [.. Current.Select(c => ContactsListRules.FullName(c) + (ContactsJson.Emails(c).FirstOrDefault() is { } e ? " (" + e + ")" : string.Empty))];

    public async Task<Result<Unit>> MergeAsync(CancellationToken ct)
    {
        if (_contacts.AccountKey is not { } key || Plan() is not { } plan)
        {
            Error = "Merging is not available yet.";
            return Result<Unit>.Ok(default);
        }

        Busy = true;
        Error = string.Empty;
        try
        {
            var result = await _writer.MergeAsync(key, plan, _contacts.GroupCards, ct).ConfigureAwait(true);
            if (result.IsOk)
            {
                await _contacts.RefreshAsync(ct).ConfigureAwait(true);
                Status = "Merged " + (plan.Others.Count + 1) + " cards into one.";
                if (Sets.Count == 0)
                {
                    Close(true);
                }
                else
                {
                    Index = Math.Min(_index, Sets.Count - 1);
                    OnPropertyChanged(string.Empty);
                }
            }
            else
            {
                Error = Words(result.Error!);
            }

            return result;
        }
        finally
        {
            Busy = false;
        }
    }
}
