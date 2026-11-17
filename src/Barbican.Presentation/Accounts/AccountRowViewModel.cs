// Mirrors: Bulwark components/layout/account-switcher.tsx:455-:500 (avatar, check on the active one, the default star only when there are two
// or more accounts, the unread badge, the server line only for an account in trouble or when two accounts share an address); spec I-006.

using Barbican.Core.Accounts;
using Barbican.Core.Mail.Rules;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Accounts;

/// <summary>One row of the account popover. Updated in place, so the list never rebuilds while it is shown.</summary>
public sealed class AccountRowViewModel(string key) : ObservableObject
{
    private string _title = "";
    private string _initial = "";
    private string _initials = "";
    private int _tintIndex;
    private bool _needsSignIn;
    private bool _isActive;
    private bool _showStar;
    private int? _unread;
    private string? _serverLine;
    private bool _hasError;
    private bool _isReadOnly;

    /// <summary>The account key.</summary>
    public string Key { get; } = key;

    /// <summary>The address.</summary>
    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    /// <summary>The avatar letter.</summary>
    public string Initial
    {
        get => _initial;
        private set => SetProperty(ref _initial, value);
    }

    /// <summary>Two letters for the avatar (Rust mail.rs initials, as the message rows).</summary>
    public string Initials
    {
        get => _initials;
        private set => SetProperty(ref _initials, value);
    }

    /// <summary>0..3: which tint the avatar takes (Rust mail.rs tint).</summary>
    public int TintIndex
    {
        get => _tintIndex;
        private set => SetProperty(ref _tintIndex, value);
    }

    /// <summary>The server refused the account: a click offers the sign-in form for it (R2-01).</summary>
    public bool NeedsSignIn
    {
        get => _needsSignIn;
        private set => SetProperty(ref _needsSignIn, value);
    }

    /// <summary>The check on the avatar.</summary>
    public bool IsActive
    {
        get => _isActive;
        private set => SetProperty(ref _isActive, value);
    }

    /// <summary>The default star.</summary>
    public bool ShowStar
    {
        get => _showStar;
        private set => SetProperty(ref _showStar, value);
    }

    /// <summary>Unread in its inbox; null shows no badge (unknown or zero).</summary>
    public int? Unread
    {
        get => _unread;
        private set
        {
            if (SetProperty(ref _unread, value))
            {
                OnPropertyChanged(nameof(UnreadText));
            }
        }
    }

    /// <summary>The badge text ("99+" above 99, Bulwark UnreadBadge).</summary>
    public string? UnreadText => Unread is > 99 ? "99+" : Unread?.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The server line, or null when it is not shown.</summary>
    public string? ServerLine
    {
        get => _serverLine;
        private set => SetProperty(ref _serverLine, value);
    }

    /// <summary>The account could not connect (the line shows an alert icon).</summary>
    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    /// <summary>Writes are refused for this account.</summary>
    public bool IsReadOnly
    {
        get => _isReadOnly;
        private set => SetProperty(ref _isReadOnly, value);
    }

    /// <summary>The error of the account ("needs to sign in again", "could not reach the server"), or null.</summary>
    public string? Error { get; private set; }

    /// <summary>Takes the state of <paramref name="status"/>.</summary>
    public void Update(AccountStatus status, int rowCount, bool sharesAddress)
    {
        ArgumentNullException.ThrowIfNull(status);
        Title = status.Account.User;
        Initial = status.Account.User.Length > 0 ? char.ToUpperInvariant(status.Account.User[0]).ToString() : "?";
        Initials = RowText.Initials(status.Account.User);
        TintIndex = RowText.TintIndex(status.Account.User);
        NeedsSignIn = status.NeedsSignIn;
        IsActive = status.IsActive;
        ShowStar = status.IsDefault && rowCount > 1;
        Unread = status.InboxUnread is > 0 ? status.InboxUnread : null;
        HasError = status.Error is not null;
        if (!string.Equals(Error, status.Error, StringComparison.Ordinal))
        {
            Error = status.Error;
            OnPropertyChanged(nameof(Error));
        }

        IsReadOnly = status.Account.IsReadOnly;
        ServerLine = HasError || sharesAddress ? MailServerAddress.ServerHostLabel(status.Account.Server) : null;
    }
}
