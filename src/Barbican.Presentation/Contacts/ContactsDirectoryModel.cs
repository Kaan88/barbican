// Mirrors: Rust contacts_x.rs:858 fetch_directory, :909 principal_card; Bulwark components/contacts/directory-browser.tsx (the principals of the server: accounts and groups, search, pick); RFC 9670 Principal; B-CON-025 (the Principal/get fallback); I-368, I-369; D-M5-13. Owner con-ui.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Contacts;

namespace Barbican.Presentation.Contacts;

/// <summary>The directory browser: load the principals once, search them, tick the ones to address.</summary>
public sealed class ContactsDirectoryModel : ContactsDialogModel
{
    private readonly IContactDirectory _directory;
    private readonly string _accountKey;
    private string _search = string.Empty;
    private bool _loaded;

    public ContactsDirectoryModel(IContactDirectory directory, string accountKey)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(accountKey);
        _directory = directory;
        _accountKey = accountKey;
    }

    public IReadOnlyList<ContactsPick> Picks { get; private set; } = [];

    public string Search
    {
        get => _search;
        set => SetProperty(ref _search, value ?? string.Empty);
    }

    public bool IsLoaded => _loaded;

    public IEnumerable<ContactsPick> Visible => Picks.Where(p => ContactsListRules.Fold(p.Name + " " + p.Email).Contains(ContactsListRules.Fold(_search), StringComparison.Ordinal));

    public async Task LoadAsync(CancellationToken ct)
    {
        Busy = true;
        Error = string.Empty;
        try
        {
            var result = await _directory.LoadAsync(_accountKey, ct).ConfigureAwait(true);
            if (!result.IsOk)
            {
                Error = Words(result.Error!);
                return;
            }

            Picks = [.. result.Value.Principals.Select(Pick).Where(p => !string.IsNullOrEmpty(p.Email)).OrderBy(p => ContactsListRules.Fold(p.Name), StringComparer.Ordinal)];
            Status = result.Value.Fallback ? "The server does not list its directory; showing what it shared." : string.Empty;
            _loaded = true;
        }
        catch (PortPendingException e)
        {
            Error = "The directory is not available yet (" + e.Message + ")";
        }
        finally
        {
            Busy = false;
            OnPropertyChanged(string.Empty);
        }
    }

    /// <summary>The ticked addresses joined for a recipient field.</summary>
    public string Chosen => string.Join(", ", Picks.Where(p => p.Selected).Select(p => p.Email));

    private static ContactsPick Pick(JsonNode principal) => new(ContactsJson.Id(principal), ContactsJson.Str(principal, "name"), ContactsJson.Str(principal, "email"), false);
}
