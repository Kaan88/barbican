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

    public SrvUiRecoverViewModel(ISrvUiRecoverSource source, IAccountManager? accounts = null, IDispatcher? dispatcher = null)
        : base(accounts, dispatcher)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    public IReadOnlyList<ArchivedItem> Items => _items;

    public bool Available => _source.Available;

    public string Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        IsLoading = true;
        var account = AccountKey;
        var result = await _source.ListAsync(ct).ConfigureAwait(false);
        await OnUiAsync(account, () =>
        {
            if (result.IsOk)
            {
                Error = null;
                _items = result.Value!;
                Raise(nameof(Items));
            }
            else
            {
                Error = result.Error!.Message;
            }

            IsLoading = false;
        }).ConfigureAwait(false);
    }

    public async Task RestoreAsync(string id, CancellationToken ct)
    {
        var account = AccountKey;
        var result = await _source.RestoreAsync(id, ct).ConfigureAwait(false);
        await OnUiAsync(account, () =>
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
            await OnUiAsync(account, () => LoadAsync(ct)).ConfigureAwait(false);
        }
    }
}
