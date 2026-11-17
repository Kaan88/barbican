// Mirrors: Bulwark components/layout/account-switcher.tsx:300-:335 (the avatar button: tooltip with name, server and "N unread in other
// accounts", a dot when other accounts have unread), :453 (a row click switches), :618 (Add account); Rust main.rs:816 on_switch_account
// (a failed switch keeps the current account and says why), :838 on_add_account ("Sign in to another account. Your other accounts stay signed
// in."). spec I-001, I-002, I-005, I-006, I-010. Fable review 2 R2-03: the account popover binds to this (the App's own popover model is gone),
// so the unread badges, the default star and the "needs to sign in again" lines follow StatusesChanged.

using System.Collections.ObjectModel;
using Barbican.Core.Accounts;
using Barbican.Presentation.Actions;
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
    private readonly IAccountMaintenance? _maintenance;
    private readonly IActionConfirmer? _confirmer;
    private bool _isOpen;
    private string _activeTitle = "";
    private string _activeInitial = "";
    private int _othersUnread;
    private string? _message;
    private bool _isSwitching;

    public AccountsViewModel(IAccountManager accounts, IAccountOverview overview, IDispatcher dispatcher, IAccountMaintenance? maintenance = null, IActionConfirmer? confirmer = null)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(overview);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _accounts = accounts;
        _overview = overview;
        _dispatcher = dispatcher;
        _maintenance = maintenance;
        _confirmer = confirmer;
        SetDefaultCommand = new AsyncRelayCommand(SetDefaultAsync);
        SignOutCommand = new AsyncRelayCommand(SignOutAsync);
        SignOutAllCommand = new AsyncRelayCommand(SignOutAllAsync);
        ToggleCommand = new RelayCommand(() => IsOpen = !IsOpen);
        SwitchCommand = new AsyncRelayCommand<AccountRowViewModel>(SwitchAsync);
        AddAccountCommand = new RelayCommand(AddAccount);
        _overview.StatusesChanged += OnStatusesChanged;
        Apply();
    }

    /// <summary>Raised when the user asks to add an account (the shell shows the sign-in screen with <see cref="AddAccountStatus"/>).</summary>
    public event EventHandler? AddAccountRequested;

    /// <summary>A row of an account the server refused was chosen: the shell shows the sign-in form for it (R2-01).</summary>
    public event EventHandler<SignInRequestedEventArgs>? SignInRequested;

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

    /// <summary>Makes the account on screen the default (I-013; Bulwark account-switcher.tsx:657).</summary>
    public IAsyncRelayCommand SetDefaultCommand { get; }

    /// <summary>Signs the account on screen out, after asking (I-014; Bulwark account-switcher.tsx:666).</summary>
    public IAsyncRelayCommand SignOutCommand { get; }

    /// <summary>Signs every account out, after asking (I-015; Bulwark account-switcher.tsx:675).</summary>
    public IAsyncRelayCommand SignOutAllCommand { get; }

    /// <summary>"Set as default" shows for an account that is not the default, when there are two or more (Bulwark account-switcher.tsx:651-659).</summary>
    public bool CanSetDefault => _maintenance is not null && Rows.Count > 1 && Rows.FirstOrDefault(r => r.IsActive) is { ShowStar: false };

    /// <summary>"Sign out of <i>address</i>" shows when an account is on screen.</summary>
    public bool CanSignOut => _maintenance is not null && Rows.Any(r => r.IsActive);

    /// <summary>"Sign out of all accounts" shows with two or more accounts (Bulwark account-switcher.tsx:672).</summary>
    public bool CanSignOutAll => _maintenance is not null && Rows.Count > 1;

    /// <summary>The label of the sign-out row.</summary>
    public string SignOutLabel => "Sign out of " + (ActiveTitle.Length > 0 ? ActiveTitle : "this account");

    /// <summary>Alt+Shift+Up/Down on a row moves it (Bulwark account-switcher.tsx:454, handleMoveKey; only a non-default account, with three or more accounts).</summary>
    public async Task MoveAsync(AccountRowViewModel row, int delta)
    {
        ArgumentNullException.ThrowIfNull(row);
        var at = Rows.IndexOf(row);
        var to = at + delta;
        if (_maintenance is null || at < 0 || row.ShowStar || Rows.Count <= 2 || to < 1 || to >= Rows.Count)
        {
            return;
        }

        var r = await _maintenance.MoveAsync(row.Key, to, CancellationToken.None).ConfigureAwait(true);
        Message = r.IsOk ? null : r.Error!.Message;
    }

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
        OnPropertyChanged(nameof(CanSetDefault));
        OnPropertyChanged(nameof(CanSignOut));
        OnPropertyChanged(nameof(CanSignOutAll));
        OnPropertyChanged(nameof(SignOutLabel));
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
        IsOpen = false;
        if (row is null)
        {
            return;
        }

        if (_overview.Statuses.FirstOrDefault(s => string.Equals(s.Account.Key, row.Key, StringComparison.Ordinal)) is { NeedsSignIn: true } refused)
        {
            // switching would fail without sending anything: offer the sign-in for it instead (also for the account on screen)
            Message = null;
            SignInRequested?.Invoke(this, new SignInRequestedEventArgs(refused));
            return;
        }

        if (row.IsActive)
        {
            return;
        }

        Message = null;

        IsSwitching = true;
        try
        {
            var r = await _accounts.SwitchToAsync(row.Key, CancellationToken.None).ConfigureAwait(true);
            if (!r.IsOk)
            {
                Message = r.Error!.Message;
            }
        }
#pragma warning disable CA1031 // the top of a row's command: a failure is shown in the popover, it must not end the app (R2-03)
        catch (Exception e)
#pragma warning restore CA1031
        {
            Message = e.Message;
        }
        finally
        {
            IsSwitching = false;
        }
    }

    private async Task SetDefaultAsync()
    {
        IsOpen = false;
        if (_maintenance is null || Rows.FirstOrDefault(r => r.IsActive) is not { } row)
        {
            return;
        }

        var r = await _maintenance.SetDefaultAsync(row.Key, CancellationToken.None).ConfigureAwait(true);
        Message = r.IsOk ? null : r.Error!.Message;
    }

    private async Task SignOutAsync()
    {
        IsOpen = false;
        if (_maintenance is null || Rows.FirstOrDefault(r => r.IsActive) is not { } row)
        {
            return;
        }

        if (!await AskAsync($"Sign out of {row.Title}?", "Its mail stays on the server. You sign in again to read it here.").ConfigureAwait(true))
        {
            return;
        }

        var r = await _maintenance.SignOutAsync(row.Key, CancellationToken.None).ConfigureAwait(true);
        Message = r.IsOk ? null : r.Error!.Message;
    }

    private async Task SignOutAllAsync()
    {
        IsOpen = false;
        if (_maintenance is null || !await AskAsync("Sign out of every account?", "Their mail stays on the servers. You sign in again to read it here.").ConfigureAwait(true))
        {
            return;
        }

        var r = await _maintenance.SignOutAllAsync(CancellationToken.None).ConfigureAwait(true);
        Message = r.IsOk ? null : r.Error!.Message;
    }

    // a destructive row always asks (the ACCOUNT menu's confirmation label, spec/menus.md); without a dialog host it does not go ahead
    private async Task<bool> AskAsync(string title, string message) =>
        _confirmer is not null && await _confirmer.ConfirmAsync(new ConfirmRequest(title, message, "Sign out"), CancellationToken.None).ConfigureAwait(true);

    private void AddAccount()
    {
        IsOpen = false;
        AddAccountRequested?.Invoke(this, EventArgs.Empty);
    }
}
