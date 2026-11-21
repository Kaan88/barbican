// Mirrors: Rust assistant column, M6 plan section 4.4 and D-M6-B03; B-AI-006, B-AI-010 ("this email" is the open message); decision D-M6b-08 (the generic add-on chat panel over IPluginChat). THREADING: IPluginChat.Changed arrives on a pool thread and is posted to the dispatcher first (D-M6-00).

using System.ComponentModel;
using Barbican.Core.Common;
using Barbican.Plugins;
using Barbican.Presentation.Common;

using Barbican.Presentation.Settings.Ui;

namespace Barbican.Presentation.Settings.Addons;

/// <summary>The state of an add-on chat panel.</summary>
public sealed class AddonChatViewModel : INotifyPropertyChanged
{
    private readonly IPluginChat _session;
    private readonly ISetUiViewModel _screen;
    private readonly IDispatcher? _dispatcher;
    private long _accountVersion;
    private bool _busy;
    private string _error = string.Empty;

    public AddonChatViewModel(IPluginChat session, ISetUiViewModel screen, IDispatcher? dispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(screen);
        _session = session;
        _screen = screen;
        _dispatcher = dispatcher;
        _session.Changed += (_, _) => OnUi(() => Raise(null));
        _screen.PropertyChanged += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.PropertyName) || string.Equals(e.PropertyName, nameof(ISetUiViewModel.AccountKey), StringComparison.Ordinal))
            {
                OnUi(() =>
                {
                    _accountVersion++;
                    _busy = false;
                    _error = string.Empty;
                    Raise(nameof(AccountKey));
                    Raise(nameof(Busy));
                    Raise(nameof(Error));
                    Raise(nameof(Turns));
                    _ = InitializeAsync(CancellationToken.None);
                });
            }
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string AccountKey => _screen.AccountKey;

    public bool Busy => _busy;

    public string Error => _error;

    /// <summary>The chat the panel shows.</summary>
    public IPluginChat Chat => _session;

    /// <summary>The visible turns: what the user said and what the add-on answered (tool traffic is not shown).</summary>
    public IReadOnlyList<PluginChatTurn> Turns => _session.Turns(_screen.AccountKey);

    /// <summary>Loads the current account's history without touching the UI thread.</summary>
    public async Task InitializeAsync(CancellationToken ct)
    {
        var account = _screen.AccountKey;
        try
        {
            await _session.InitializeAsync(account, ct).ConfigureAwait(false);
            OnUi(() =>
            {
                if (string.Equals(account, _screen.AccountKey, StringComparison.Ordinal))
                {
                    Raise(nameof(Turns));
                }
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The view closed while loading.
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            OnUi(() =>
            {
                if (string.Equals(account, _screen.AccountKey, StringComparison.Ordinal))
                {
                    _error = "Could not load the conversation.";
                    Raise(nameof(Error));
                }
            });
        }
    }

    public async Task AskAsync(string question, string? openMessageId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(question) || _busy)
        {
            return;
        }

        var account = _screen.AccountKey;
        var version = _accountVersion;
        _busy = true;
        _error = string.Empty;
        Raise(null);
        try
        {
            var r = await _session.SendAsync(account, question.Trim(), openMessageId, ct).ConfigureAwait(true);
            if (Current(account, version) && r.Error is not null)
            {
                _error = r.Error;
            }
        }
        catch (PortPendingException)
        {
            if (Current(account, version)) { _error = "This add-on is not available yet."; }
        }

        finally
        {
            if (Current(account, version)) { _busy = false; Raise(null); }
        }
    }

    public async Task ClearAsync(CancellationToken ct)
    {
        var account = _screen.AccountKey;
        var version = _accountVersion;
        try
        {
            await _session.ClearAsync(account, ct).ConfigureAwait(true);
        }
        catch (PortPendingException)
        {
            // nothing to clear
        }

        if (Current(account, version)) { _error = string.Empty; Raise(null); }
    }

    private bool Current(string account, long version) => version == _accountVersion && string.Equals(account, _screen.AccountKey, StringComparison.Ordinal);

    private void OnUi(Action action)
    {
        if (_dispatcher is null || _dispatcher.HasThreadAccess)
        {
            action();
        }
        else
        {
            _dispatcher.Post(action);
        }
    }

    private void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
