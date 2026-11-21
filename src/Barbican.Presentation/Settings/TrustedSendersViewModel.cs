// Mirrors: Bulwark components/trusted-senders-modal.tsx (the list, the search with 5 or more senders, the add row with its errors, remove
// without a question), stores/settings-store.ts:2171-2201 (addTrustedSender / removeTrustedSender over the lower-cased list);
// locales/en/common.json settings.email_behavior.trusted_senders. Owner reader, I-209.

using Barbican.Core.Mail.Trust;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Settings;

/// <summary>The trusted senders dialog: the list, its search, the add row and removal. Every change goes through <see cref="ITrustedSenders"/>, so it is on disk.</summary>
public sealed class TrustedSendersViewModel : ObservableObject
{
    private readonly ITrustedSenders _trust;
    private string _query = string.Empty;
    private string _newAddress = string.Empty;
    private string _error = string.Empty;
    private bool _adding;

    public TrustedSendersViewModel(ITrustedSenders trust)
    {
        ArgumentNullException.ThrowIfNull(trust);
        _trust = trust;
    }

    /// <summary>The addresses shown, in list order, filtered by <see cref="Query"/>.</summary>
    public IReadOnlyList<string> Addresses => TrustedSendersRules.Filter(_trust.All, _query);

    /// <summary>"None", "1 sender" or "N senders" (the settings row and the dialog title line).</summary>
    public string CountText => TrustedSendersRules.CountText(_trust.All.Count);

    /// <summary>The list has no sender at all (the empty state, not a search with no hits).</summary>
    public bool IsEmpty => _trust.All.Count == 0;

    /// <summary>The search box appears with five or more senders, as in Bulwark.</summary>
    public bool ShowSearch => _trust.All.Count >= TrustedSendersRules.SearchFrom;

    /// <summary>The search is set and nothing matches it.</summary>
    public bool NoResults => !IsEmpty && Addresses.Count == 0;

    /// <summary>The add row is open (the "Add sender manually" button was picked).</summary>
    public bool IsAdding
    {
        get => _adding;
        private set => SetProperty(ref _adding, value);
    }

    /// <summary>The address being added.</summary>
    public string NewAddress
    {
        get => _newAddress;
        set
        {
            if (SetProperty(ref _newAddress, value ?? string.Empty))
            {
                Error = string.Empty;
            }
        }
    }

    /// <summary>The add or remove error, or empty.</summary>
    public string Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    /// <summary>The search text.</summary>
    public string Query
    {
        get => _query;
        set
        {
            if (SetProperty(ref _query, value ?? string.Empty))
            {
                Changed();
            }
        }
    }

    /// <summary>Opens the add row and puts the cursor in it (the dialog focuses it).</summary>
    public void BeginAdd() => IsAdding = true;

    /// <summary>Closes the add row and forgets what was typed.</summary>
    public void CancelAdd()
    {
        IsAdding = false;
        NewAddress = string.Empty;
        Error = string.Empty;
    }

    /// <summary>
    /// Adds the typed address: trimmed and lower-cased, validated, and refused when already trusted (Bulwark
    /// trusted-senders-modal.tsx:109-138). False leaves the add row open with <see cref="Error"/> set.
    /// </summary>
    public async Task<bool> AddAsync(CancellationToken ct)
    {
        var address = TrustedSendersRules.Normalize(NewAddress);
        if (!TrustedSendersRules.IsValid(address))
        {
            Error = TrustedSendersRules.InvalidEmail;
            return false;
        }

        if (TrustedSendersRules.Contains(_trust.All, address))
        {
            Error = TrustedSendersRules.AlreadyAdded;
            return false;
        }

        var added = await _trust.AddAsync(address, ct).ConfigureAwait(true);
        if (!added.IsOk)
        {
            Error = added.Error?.Message ?? "Failed to save the sender.";
            return false;
        }

        NewAddress = string.Empty;
        IsAdding = false;
        Error = string.Empty;
        Changed();
        return true;
    }

    /// <summary>Removes a sender from the list; false leaves <see cref="Error"/> set.</summary>
    public async Task<bool> RemoveAsync(string address, CancellationToken ct)
    {
        var removed = await _trust.RemoveAsync(address, ct).ConfigureAwait(true);
        if (!removed.IsOk)
        {
            Error = TrustedSendersRules.RemoveFailed;
            return false;
        }

        Error = string.Empty;
        Changed();
        return true;
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(Addresses));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowSearch));
        OnPropertyChanged(nameof(NoResults));
    }
}
