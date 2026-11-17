// Mirrors: Bulwark components/layout/account-switcher.tsx (rows, switching, add account); Rust main.rs account popover; spec I-001, I-002, I-005, I-006, I-010.
// Reads the IAccountManager contract (M1-plan §4.2) directly: the accounts stream's AccountsViewModel is an empty stub while this stream is built.

using System.Collections.ObjectModel;
using Barbican.Core.Accounts;
using Barbican.Core.Mail.Rules;
using Barbican.Presentation.Common;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.App.Views.Accounts;

/// <summary>The accounts the popover lists and the two things it can do: switch to one, ask for another to be added.</summary>
public sealed class AccountPopoverModel : ObservableObject, IDisposable
{
    private readonly IAccountManager? _manager;
    private readonly IDispatcher _dispatcher;
    private string? _error;

    public AccountPopoverModel(IAccountManager? manager, IDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _manager = manager;
        _dispatcher = dispatcher;
        if (_manager is not null)
        {
            _manager.ActiveChanged += OnActiveChanged;
        }

        Refresh();
    }

    /// <summary>The rows, most recent first as the manager lists them. Only changed rows are replaced.</summary>
    public ObservableCollection<AccountRowItem> Rows { get; } = [];

    public bool HasRows => Rows.Count > 0;

    /// <summary>The last switch failure, or null.</summary>
    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    /// <summary>The user chose "Add account": the shell shows the sign-in view.</summary>
    public event EventHandler? AddAccountRequested;

    public void RequestAddAccount() => AddAccountRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Makes an account the active one (I-005); the list follows ActiveChanged.</summary>
    public async Task SwitchAsync(string key)
    {
        if (_manager is null || string.Equals(_manager.Active?.Key, key, StringComparison.Ordinal))
        {
            return;
        }

        var result = await _manager.SwitchToAsync(key, CancellationToken.None);
        Error = result.IsOk ? null : result.Error!.Message;
    }

    public void Dispose()
    {
        if (_manager is not null)
        {
            _manager.ActiveChanged -= OnActiveChanged;
        }
    }

    private void OnActiveChanged(object? sender, ActiveAccountChangedEventArgs e) => _dispatcher.Post(Refresh);

    private void Refresh()
    {
        var accounts = _manager?.Accounts ?? [];
        var active = _manager?.Active?.Key;
        var wanted = accounts.Select(a => new AccountRowItem(
            a.Key,
            a.User,
            a.Server,
            RowText.Initials(a.User),
            RowText.TintIndex(a.User),
            string.Equals(a.Key, active, StringComparison.Ordinal))).ToList();
        for (var i = 0; i < wanted.Count; i++)
        {
            if (i >= Rows.Count)
            {
                Rows.Add(wanted[i]);
            }
            else if (Rows[i] != wanted[i])
            {
                Rows[i] = wanted[i];
            }
        }

        while (Rows.Count > wanted.Count)
        {
            Rows.RemoveAt(Rows.Count - 1);
        }

        OnPropertyChanged(nameof(HasRows));
    }
}
