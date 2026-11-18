// Mirrors: Rust contacts.rs groups (commits e75e107, 4d24260: create a group with members, rename, delete), Bulwark components/contacts/group-form.tsx (name, member picker with search); B-CON-016, B-CON-024; I-359, I-360.
// The dialog of a new group and of "edit members": a name, a search field and a tick per contact. Owner con-ui.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Contacts;

namespace Barbican.Presentation.Contacts;

/// <summary>The group form.</summary>
public sealed class ContactsGroupModel : ContactsDialogModel
{
    private readonly IContactsViewModel _contacts;
    private readonly IContactWriter _writer;
    private readonly string? _groupId;
    private readonly HashSet<string> _before;
    private readonly string _originalName;
    private string _name;
    private string _search = string.Empty;

    /// <summary>A new group (<paramref name="groupId"/> null) or the members of an existing one.</summary>
    public ContactsGroupModel(IContactsViewModel contacts, IContactWriter writer, string? groupId, IEnumerable<string>? preselected = null)
    {
        ArgumentNullException.ThrowIfNull(contacts);
        ArgumentNullException.ThrowIfNull(writer);
        _contacts = contacts;
        _writer = writer;
        _groupId = groupId;
        var group = groupId is null ? null : contacts.CardOf(groupId);
        _name = group is null ? string.Empty : ContactsListRules.FullName(group);
        _originalName = _name;
        var chosen = new HashSet<string>(preselected ?? [], StringComparer.Ordinal);
        Picks =
        [
            .. contacts.Contacts
                .OrderBy(c => ContactsListRules.Fold(ContactsListRules.SortName(c, contacts.SortByLastName)), StringComparer.Ordinal)
                .Select(c => new ContactsPick(ContactsJson.Id(c), ContactsListRules.FullName(c), ContactsJson.Emails(c).FirstOrDefault(), chosen.Contains(ContactsJson.Id(c)) || (group is not null && ContactsJson.HasMember(group, c)))),
        ];
        _before = [.. Picks.Where(p => p.Selected).Select(p => p.Id)];
    }

    public bool IsNew => _groupId is null;

    public string Heading => _groupId is null ? "New group" : "Edit group";

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value ?? string.Empty);
    }

    public string Search
    {
        get => _search;
        set => SetProperty(ref _search, value ?? string.Empty);
    }

    public IReadOnlyList<ContactsPick> Picks { get; }

    /// <summary>The picks the search shows.</summary>
    public IEnumerable<ContactsPick> Visible => Picks.Where(p => ContactsListRules.Fold(p.Name + " " + p.Email).Contains(ContactsListRules.Fold(_search), StringComparison.Ordinal));

    public async Task<Result<Unit>> SaveAsync(CancellationToken ct)
    {
        var name = _name.Trim();
        if (name.Length == 0)
        {
            Error = "A group needs a name.";
            return Result<Unit>.Ok(default);
        }

        if (_contacts.AccountKey is not { } key)
        {
            return Result<Unit>.Ok(default);
        }

        Busy = true;
        Error = string.Empty;
        try
        {
            var chosen = Picks.Where(p => p.Selected).Select(p => p.Id).ToList();
            Result<Unit> result;
            if (_groupId is null)
            {
                var uids = chosen.Select(id => UidOf(id)).Where(u => u.Length > 0).ToList();
                var created = await _writer.CreateGroupAsync(key, null, name, uids, ct).ConfigureAwait(true);
                result = created.Map(_ => default(Unit));
            }
            else
            {
                var add = chosen.Where(id => !_before.Contains(id)).Select(id => UidOf(id)).Where(u => u.Length > 0).ToList();
                var remove = _before.Where(id => !chosen.Contains(id, StringComparer.Ordinal)).Select(id => UidOf(id)).Where(u => u.Length > 0).ToList();
                result = await _writer.SetMembersAsync(key, _groupId, add, remove, ct).ConfigureAwait(true);
                if (result.IsOk && !string.Equals(name, _originalName, StringComparison.Ordinal))
                {
                    var next = _contacts.CardOf(_groupId)?["name"]?.DeepClone() as JsonObject ?? [];
                    next["full"] = name;
                    result = await _writer.UpdateAsync(key, _groupId, new JsonObject { ["name"] = next }, ct).ConfigureAwait(true);
                }
            }

            if (result.IsOk)
            {
                await _contacts.RefreshAsync(ct).ConfigureAwait(true);
                Close(true);
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

    private string UidOf(string id) => _contacts.CardOf(id) is { } card ? ContactsJson.Str(card, "uid") : string.Empty;
}
