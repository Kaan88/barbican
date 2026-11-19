// Mirrors: Bulwark components/settings/recover-deleted-settings.tsx (a list of deleted items, restore per item, nothing older than the server keeps), Rust server_settings.rs:137-152 and :505-512; I-438. A restore asks the server to put the message back where it was. Owner srv-ui.

using Barbican.Core.Accounts;
using Barbican.Core.Mail.Archive;
using Barbican.Presentation.Common;

namespace Barbican.Presentation.Settings.Server;

/// <summary>The Recover page.</summary>
public sealed class SrvUiRecoverViewModel : SrvUiPageModel
{
    private readonly ISrvUiRecoverSource _source;
    private IReadOnlyList<ArchivedItem> _items = [];
    private string _message = string.Empty;
    private long _loadSequence;

    public SrvUiRecoverViewModel(ISrvUiRecoverSource source, IAccountManager? accounts = null, IDispatcher? dispatcher = null)
        : base(accounts, dispatcher)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    public IReadOnlyList<ArchivedItem> Items => IsLoaded(CaptureAccount()) ? _items : [];

    public bool Available => _source.Available(AccountKey);

    public string Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        IsLoading = true;
        var stamp = CaptureAccount();
        var sequence = Interlocked.Increment(ref _loadSequence);
        var result = await _source.ListAsync(stamp.Key, ct).ConfigureAwait(false);
        await OnUiAsync(stamp, () =>
        {
            if (sequence != Interlocked.Read(ref _loadSequence)) return;
            if (result.IsOk)
            {
                Error = null;
                _items = result.Value!;
                MarkLoaded(stamp);
                Raise(nameof(Items));
            }
            else
            {
                Error = result.Error!.Message;
            }

            IsLoading = false;
        }).ConfigureAwait(false);
    }

    public Task RestoreAsync(string id, CancellationToken ct) => RestoreAsync(id, AccountGeneration, ct);

    public async Task RestoreAsync(string id, long expectedGeneration, CancellationToken ct)
    {
        if (!RequireLoaded(out var stamp) || stamp.Generation != expectedGeneration || !_items.Any(item => string.Equals(item.Id, id, StringComparison.Ordinal))) return;
        var result = await _source.RestoreAsync(stamp.Key, id, ct).ConfigureAwait(false);
        await OnUiAsync(stamp, () =>
        {
            if (!result.IsOk)
            {
                Error = result.Error!.Message;
            }
            else
            {
                Message = "Restored. The message is back where it was.";
            }
        }).ConfigureAwait(false);
        if (result.IsOk)
        {
            await OnUiAsync(stamp, () => LoadAsync(ct)).ConfigureAwait(false);
        }
    }
    protected override void OnAccountSwitched()
    {
        _ = Interlocked.Increment(ref _loadSequence);
        _items = [];
        Message = string.Empty;
        Raise(nameof(Items));
        Raise(nameof(Available));
        if (Available) ReloadAfterSwitch(() => LoadAsync(CancellationToken.None));
    }

}
