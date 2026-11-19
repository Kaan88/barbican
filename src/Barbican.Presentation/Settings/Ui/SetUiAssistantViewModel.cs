// Mirrors: Rust assistant column, M6 plan section 4.4 and D-M6-B03 (the column and its page use IAssistantSession); B-AI-006, B-AI-010 ("this email" is the open message). THREADING: IAssistantSession.Changed arrives on a pool thread and is posted to the dispatcher first (D-M6-00).

using System.ComponentModel;
using Barbican.Core.Common;
using Barbican.Core.Settings.Assistant;
using Barbican.Presentation.Common;

namespace Barbican.Presentation.Settings.Ui;

/// <summary>The state of the assistant column.</summary>
public sealed class SetUiAssistantViewModel : INotifyPropertyChanged
{
    private readonly IAssistantSession _session;
    private readonly ISetUiViewModel _screen;
    private readonly IDispatcher? _dispatcher;
    private long _accountVersion;
    private bool _busy;
    private string _error = string.Empty;

    public SetUiAssistantViewModel(IAssistantSession session, ISetUiViewModel screen, IDispatcher? dispatcher = null)
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
                    Raise(nameof(Messages));
                    _ = InitializeAsync(CancellationToken.None);
                });
            }
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string AccountKey => _screen.AccountKey;

    public bool Busy => _busy;

    public string Error => _error;

    /// <summary>The visible turns: what the user said and what the assistant answered (tool traffic is not shown).</summary>
    public IReadOnlyList<AssistantMessage> Messages
    {
        get
        {
            try
            {
                return [.. _session.History(_screen.AccountKey).Where(m => m.Role is "user" or "assistant" && !string.IsNullOrWhiteSpace(m.Text))];
            }
            catch (PortPendingException)
            {
                return [];
            }
        }
    }

    /// <summary>Loads the current account's history without touching the UI thread.</summary>
    public async Task InitializeAsync(CancellationToken ct)
    {
        var account = _screen.AccountKey;
        try
        {
            await _session.InitializeHistoryAsync(account, ct).ConfigureAwait(false);
            OnUi(() =>
            {
                if (string.Equals(account, _screen.AccountKey, StringComparison.Ordinal))
                {
                    Raise(nameof(Messages));
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
                    _error = "Could not load assistant history.";
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
            var r = await _session.AskAsync(account, question.Trim(), openMessageId, ct).ConfigureAwait(true);
            if (Current(account, version) && !r.IsOk)
            {
                _error = r.Error?.Message ?? "The assistant did not answer.";
            }
        }
        catch (PortPendingException)
        {
            if (Current(account, version)) { _error = "The assistant is not available yet."; }
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
            _ = await _session.ClearAsync(account, ct).ConfigureAwait(true);
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
