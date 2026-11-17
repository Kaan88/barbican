// Mirrors: Bulwark components/layout/account-switcher.tsx:300-:335 (the avatar button: tooltip with name, server and "N unread in other
// accounts", a dot when other accounts have unread), :453 (a row click switches), :618 (Add account); Rust main.rs:816 on_switch_account
// (a failed switch keeps the current account and says why), :838 on_add_account ("Sign in to another account. Your other accounts stay signed
// in."). spec I-001, I-002, I-005, I-006, I-010.

using System.Collections.ObjectModel;
using Barbican.Core.Accounts;
using Barbican.Presentation.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Barbican.Presentation.Accounts;

/// <summary>The rail avatar and the account popover.</summary>
public sealed class AccountsViewModel : ObservableObject, IDisposable
{
    private readonly IAccountManager _accounts;
    private readonly IAccountOverview _overview;
    private readonly IDispatcher _dispatcher;
    private bool _isOpen;
    private string _activeTitle = "";
    private string _activeInitial = "";
    private int _othersUnread;
    private string? _message;
    private bool _isSwitching;

    public AccountsViewModel(IAccountManager accounts, IAccountOverview overview, IDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(overview);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _accounts = accounts;
        _overview = overview;
        _dispatcher = dispatcher;
        ToggleCommand = new RelayCommand(() => IsOpen = !IsOpen);
        SwitchCommand = new AsyncRelayCommand<AccountRowViewModel>(SwitchAsync);
        AddAccountCommand = new RelayCommand(AddAccount);
        _overview.StatusesChanged += OnStatusesChanged;
        Apply();
    }

    /// <summary>Raised when the user asks to add an account (the shell shows the sign-in screen with <see cref="AddAccountStatus"/>).</summary>
    public event EventHandler? AddAccountRequested;

    /// <summary>The status the sign-in screen shows when adding an account (Rust main.rs:842).</summary>
    public static string AddAccountStatus => "Sign in to another account. Your other accounts stay signed in.";

    /// <summary>The rows, most recently used first; rows are updated in place.</summary>
    public ObservableCollection<AccountRowViewModel> Rows { get; } = [];

    /// <summary>Opens and closes the popover (I-001).</summary>
    public IRelayCommand ToggleCommand { get; }

    /// <summary>Switches to a row's account (I-005).</summary>
    public IAsyncRelayCommand<AccountRowViewModel> SwitchCommand { get; }

    /// <summary>Add account (I-010).</summary>
    public IRelayCommand AddAccountCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        set => SetProperty(ref _isOpen, value);
    }

    /// <summary>The active account's address (the avatar's tooltip and label).</summary>
    public string ActiveTitle
    {
        get => _activeTitle;
        private set => SetProperty(ref _activeTitle, value);
    }

    /// <summary>The active account's avatar letter.</summary>
    public string ActiveInitial
    {
        get => _activeInitial;
        private set => SetProperty(ref _activeInitial, value);
    }

    /// <summary>Unread waiting in the accounts not on screen (I-002).</summary>
    public int OthersUnread
    {
        get => _othersUnread;
        private set
        {
            if (SetProperty(ref _othersUnread, value))
            {
                OnPropertyChanged(nameof(HasOthersUnread));
                OnPropertyChanged(nameof(Tooltip));
            }
        }
    }

    /// <summary>The dot on the avatar.</summary>
    public bool HasOthersUnread => OthersUnread > 0;

    /// <summary>The avatar tooltip: address, server, and the others' unread (I-002).</summary>
    public string Tooltip
    {
        get
        {
            var active = Rows.FirstOrDefault(r => r.IsActive);
            var lines = new List<string>();
            if (active is not null)
            {
                lines.Add(active.Title);
                if (_accounts.Active is { } a)
                {
                    lines.Add(MailServerAddress.ServerHostLabel(a.Server));
                }
            }

            if (OthersUnread > 0)
            {
                lines.Add(OthersUnread == 1 ? "1 unread in other accounts" : $"{OthersUnread} unread in other accounts");
            }

            return string.Join('\n', lines);
        }
    }

    /// <summary>A switch is running (the popover row shows progress; the old account stays on screen).</summary>
    public bool IsSwitching
    {
        get => _isSwitching;
        private set => SetProperty(ref _isSwitching, value);
    }

    /// <summary>What went wrong with the last switch, or null.</summary>
    public string? Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public void Dispose() => _overview.StatusesChanged -= OnStatusesChanged;

    private void OnStatusesChanged(object? sender, EventArgs e)
    {
        if (_dispatcher.HasThreadAccess)
        {
            Apply();
        }
        else
        {
            _dispatcher.Post(Apply);
        }
    }

    // in place: existing rows keep their objects (no flicker in an open popover), new ones are inserted, gone ones removed
    private void Apply()
    {
        var statuses = _overview.Statuses;
        for (var i = 0; i < statuses.Count; i++)
        {
            var s = statuses[i];
            var shares = statuses.Count(o => string.Equals(o.Account.User, s.Account.User, StringComparison.OrdinalIgnoreCase)) > 1;
            var at = IndexOf(s.Account.Key);
            var row = at >= 0 ? Rows[at] : new AccountRowViewModel(s.Account.Key);
            row.Update(s, statuses.Count, shares);
            if (at < 0)
            {
                Rows.Insert(i, row);
            }
            else if (at != i)
            {
                Rows.Move(at, i);
            }
        }

        while (Rows.Count > statuses.Count)
        {
            Rows.RemoveAt(Rows.Count - 1);
        }

        var active = statuses.FirstOrDefault(s => s.IsActive);
        ActiveTitle = active?.Account.User ?? "";
        ActiveInitial = active is null || active.Account.User.Length == 0 ? "" : char.ToUpperInvariant(active.Account.User[0]).ToString();
        OthersUnread = InboxUnread.Total(statuses.Where(s => !s.IsActive && s.IsConnected));
        OnPropertyChanged(nameof(Tooltip));
    }

    private int IndexOf(string key)
    {
        for (var i = 0; i < Rows.Count; i++)
        {
            if (string.Equals(Rows[i].Key, key, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private async Task SwitchAsync(AccountRowViewModel? row)
    {
        if (row is null || row.IsActive)
        {
            IsOpen = false;
            return;
        }

        IsOpen = false;
        IsSwitching = true;
        Message = null;
        try
        {
            var r = await _accounts.SwitchToAsync(row.Key, CancellationToken.None).ConfigureAwait(true);
            if (!r.IsOk)
            {
                Message = r.Error!.Message;
            }
        }
        finally
        {
            IsSwitching = false;
        }
    }

    private void AddAccount()
    {
        IsOpen = false;
        AddAccountRequested?.Invoke(this, EventArgs.Empty);
    }
}
