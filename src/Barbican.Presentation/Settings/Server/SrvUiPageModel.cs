// Mirrors: M6 plan D-M6-00 (threading contract). The base of the srv-ui page view models: the active account's key, the loading flag and the error line, and a PropertyChanged that is always raised on the UI thread (the services raise Changed from a pool thread and complete off the UI thread).
// Owner srv-ui.

using System.ComponentModel;
using System.Runtime.CompilerServices;
using Barbican.Core.Accounts;
using Barbican.Presentation.Common;

namespace Barbican.Presentation.Settings.Server;

/// <summary>Shared state of the server-backed settings pages.</summary>
public abstract class SrvUiPageModel : INotifyPropertyChanged
{
    /// <summary>The account key of a sample-data run (no signed-in account).</summary>
    public const string SampleKey = "sample";

    private readonly IAccountManager? _accounts;
    private readonly IDispatcher? _dispatcher;
    private bool _loading;
    private string? _error;
    private long _accountGeneration;
    private AccountStamp? _loaded;

    protected SrvUiPageModel(IAccountManager? accounts, IDispatcher? dispatcher)
    {
        _accounts = accounts;
        _dispatcher = dispatcher;
        if (accounts is not null) accounts.ActiveChanged += (_, _) => AccountSwitched();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The account the page edits: the active one, or the sample account.</summary>
    public string AccountKey => _accounts?.Active?.Key ?? SampleKey;

    public bool IsLoading
    {
        get => _loading;
        protected set => Set(ref _loading, value);
    }

    /// <summary>The last failure of a load or a write, or null.</summary>
    public string? Error
    {
        get => _error;
        protected set => Set(ref _error, value);
    }

    /// <summary>Account and switch generation captured together for an operation.</summary>
    protected readonly record struct AccountStamp(string Key, long Generation);

    /// <summary>Generation used by dialogs to reject a confirmation after any account switch.</summary>
    public long AccountGeneration => Interlocked.Read(ref _accountGeneration);

    public bool IsCurrentGeneration(long generation) => generation == AccountGeneration && IsLoaded(CaptureAccount());

    protected AccountStamp CaptureAccount() => new(AccountKey, AccountGeneration);

    protected bool IsCurrent(AccountStamp stamp) => stamp.Generation == AccountGeneration && IsMine(stamp.Key);

    protected bool IsLoaded(AccountStamp stamp) => IsCurrent(stamp) && _loaded is { } loaded && loaded == stamp;

    protected bool RequireLoaded(out AccountStamp stamp)
    {
        stamp = CaptureAccount();
        if (IsLoaded(stamp) && !IsLoading) return true;
        Error = "Wait for this account's settings to load.";
        return false;
    }

    protected void MarkLoaded(AccountStamp stamp) => _loaded = stamp;

    protected virtual void OnAccountSwitched() { }

    protected void ReloadAfterSwitch(Func<Task> load) => _ = ReloadAfterSwitchAsync(CaptureAccount(), load);

    private async Task ReloadAfterSwitchAsync(AccountStamp stamp, Func<Task> load)
    {
        try { await load().ConfigureAwait(false); }
#pragma warning disable CA1031 // A background account-change load must surface errors rather than fault unobserved.
        catch (Exception error)
#pragma warning restore CA1031
        {
            await OnUiAsync(stamp, () => { IsLoading = false; Error = error.Message; }).ConfigureAwait(false);
        }
    }

    private void AccountSwitched()
    {
        _ = Interlocked.Increment(ref _accountGeneration);
        var stamp = CaptureAccount();
        _ = OnUiAsync(stamp, () =>
        {
            _loaded = null;
            Error = null;
            IsLoading = false;
            OnAccountSwitched();
        });
    }

    protected Task OnUiAsync(AccountStamp stamp, Action action) => OnUiAsync(stamp.Key, () => { if (IsCurrent(stamp)) action(); });

    protected Task OnUiAsync(AccountStamp stamp, Func<Task> action) => OnUiAsync(stamp.Key, () => IsCurrent(stamp) ? action() : Task.CompletedTask);

    /// <summary>True when the event is about the account the page shows.</summary>
    protected bool IsMine(string accountKey) => string.Equals(accountKey, AccountKey, StringComparison.Ordinal);

    /// <summary>Raises PropertyChanged on the UI thread, whichever thread asks.</summary>
    protected void Raise([CallerMemberName] string? name = null)
    {
        if (_dispatcher is null || _dispatcher.HasThreadAccess)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        else
        {
            _dispatcher.Post(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)));
        }
    }

    /// <summary>Applies a service result on the UI dispatcher, only for its original account.</summary>
    protected Task OnUiAsync(string accountKey, Action action) => _dispatcher is null
        ? ApplyAsync(accountKey, action)
        : _dispatcher.InvokeAsync(() => ApplyAsync(accountKey, action));

    protected Task OnUiAsync(string accountKey, Func<Task> action) => _dispatcher is null
        ? ApplyAsync(accountKey, action)
        : _dispatcher.InvokeAsync(() => ApplyAsync(accountKey, action));

    private Task ApplyAsync(string accountKey, Func<Task> action) => IsMine(accountKey) ? action() : Task.CompletedTask;

    private Task ApplyAsync(string accountKey, Action action)
    {
        if (IsMine(accountKey))
        {
            action();
        }

        return Task.CompletedTask;
    }

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        Raise(name);
    }
}
