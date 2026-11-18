// Mirrors: Bulwark components/settings/identity-settings.tsx and identity-manager-modal.tsx (add, primary = first, delete, reply-to, Bcc, text and HTML signature), Rust identities; I-291. M6 plan section 4.5 (IAccountIdentities, set-data). Deleting asks first (D-7); the page asks, this model deletes.
// Owner srv-ui.

using Barbican.Core.Accounts;
using Barbican.Core.Settings.Account;
using Barbican.Presentation.Common;

namespace Barbican.Presentation.Settings.Server;

/// <summary>The identities page: the list in the user's order and the form of the selected one.</summary>
public sealed class SrvUiIdentitiesViewModel : SrvUiPageModel
{
    private readonly IAccountIdentities _service;
    private IReadOnlyList<AccountIdentity> _items = [];
    private string? _selectedId;
    private bool _creating;

    public SrvUiIdentitiesViewModel(IAccountIdentities service, IAccountManager? accounts = null, IDispatcher? dispatcher = null)
        : base(accounts, dispatcher)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        _service.Changed += (_, e) =>
        {
            _ = ReloadQuietlyAsync(e.AccountKey);
        };
    }

    public IReadOnlyList<AccountIdentity> Items => _items;

    /// <summary>The id being edited, or null for a new identity.</summary>
    public string? SelectedId => _selectedId;

    public bool IsNew => _selectedId is null;

    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string ReplyTo { get; set; } = string.Empty;

    public string Bcc { get; set; } = string.Empty;

    public string TextSignature { get; set; } = string.Empty;

    public string HtmlSignature { get; set; } = string.Empty;

    /// <summary>The selected identity can be deleted (a default identity cannot).</summary>
    public bool CanDelete => _items.FirstOrDefault(i => string.Equals(i.Id, _selectedId, StringComparison.Ordinal))?.MayDelete ?? false;

    public async Task LoadAsync(CancellationToken ct)
    {
        IsLoading = true;
        var account = AccountKey;
        var result = await _service.ListAsync(account, ct).ConfigureAwait(false);
        await OnUiAsync(account, () => { Take(result); IsLoading = false; }).ConfigureAwait(false);
    }

    /// <summary>Opens an empty form for a new identity.</summary>
    public void StartNew()
    {
        _creating = true;
        Select(null);
    }

    public void Select(string? id)
    {
        _creating = id is null && _creating;
        _selectedId = id;
        var item = _items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.Ordinal));
        Name = item?.Name ?? string.Empty;
        Email = item?.Email ?? string.Empty;
        ReplyTo = item?.ReplyTo ?? string.Empty;
        Bcc = item?.Bcc ?? string.Empty;
        TextSignature = item?.TextSignature ?? string.Empty;
        HtmlSignature = item?.HtmlSignature ?? string.Empty;
        Error = null;
        Raise(string.Empty);
    }

    public async Task SaveAsync(CancellationToken ct)
    {
        if (Email.Trim().Length == 0 || !Email.Contains('@', StringComparison.Ordinal))
        {
            Error = "Enter the address this identity sends from.";
            return;
        }

        var edit = new AccountIdentityEdit(Name.Trim(), Email.Trim(), ReplyTo.Trim(), Bcc.Trim(), TextSignature, HtmlSignature);
        var account = AccountKey;
        var result = _selectedId is null
            ? await _service.CreateAsync(account, edit, ct).ConfigureAwait(false)
            : await _service.UpdateAsync(account, _selectedId, edit, ct).ConfigureAwait(false);
        await OnUiAsync(account, async () =>
        {
            if (!result.IsOk)
            {
                Error = result.Error!.Message;
                return;
            }

            Error = null;
            _creating = false;
            _selectedId = result.Value!.Id;
            await LoadAsync(ct).ConfigureAwait(true);
        }).ConfigureAwait(false);
    }

    public async Task DeleteAsync(CancellationToken ct)
    {
        if (_selectedId is null)
        {
            return;
        }

        var account = AccountKey;
        var result = await _service.DeleteAsync(account, _selectedId, ct).ConfigureAwait(false);
        await OnUiAsync(account, async () =>
        {
            if (!result.IsOk)
            {
                Error = result.Error!.Message;
                return;
            }

            _selectedId = null;
            await LoadAsync(ct).ConfigureAwait(true);
        }).ConfigureAwait(false);
    }

    /// <summary>Moves an identity up (-1) or down (1); the first is the default sender.</summary>
    public async Task MoveAsync(string id, int direction, CancellationToken ct)
    {
        var ids = _items.Select(i => i.Id).ToList();
        var at = ids.IndexOf(id);
        var to = at + direction;
        if (at < 0 || to < 0 || to >= ids.Count)
        {
            return;
        }

        (ids[at], ids[to]) = (ids[to], ids[at]);
        var account = AccountKey;
        var result = await _service.SetOrderAsync(account, ids, ct).ConfigureAwait(false);
        await OnUiAsync(account, async () =>
        {
            if (!result.IsOk)
            {
                Error = result.Error!.Message;
                return;
            }

            await LoadAsync(ct).ConfigureAwait(true);
        }).ConfigureAwait(false);
    }

    private void Take(Barbican.Core.Common.Result<IReadOnlyList<AccountIdentity>> result)
    {
        if (!result.IsOk)
        {
            Error = result.Error!.Message;
            return;
        }

        Error = null;
        _items = result.Value!;
        if (_creating)
        {
            Raise(nameof(Items));
            return;
        }

        var keep = _selectedId is not null && _items.Any(i => string.Equals(i.Id, _selectedId, StringComparison.Ordinal)) ? _selectedId : _items.Count > 0 ? _items[0].Id : null;
        Select(keep);
    }

    private async Task ReloadQuietlyAsync(string account)
    {
        try
        {
            var result = await _service.ListAsync(account, CancellationToken.None).ConfigureAwait(false);
            await OnUiAsync(account, () => Take(result)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // the page went away
        }
    }
}
