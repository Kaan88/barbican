// Mirrors: Rust mailto.rs dispatch :134 / try_open :150 / retry :163 / fill :180 (a link opens the composer, or waits for a sign-in and tells the user), Bulwark lib/protocol-handlers/mailto.ts and mail-app.tsx:7298 (the account picker),
// lib/plugin-hook-catalog.ts:103,105 (onMailtoIntercept, onComposeFromLink); B-COMP-034 (a link clicked in a message opens Barbican's composer, never the OS handler); I-283.
// One router for every mailto link: from the command line, from another launch (the hand-off), from a message. Owner composer-ui.

using Barbican.Core.Accounts;
using Barbican.Core.Compose;
using Barbican.Core.Mailto;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Dialogs;

namespace Barbican.Presentation.Compose;

/// <summary>Routes mailto links to the composer.</summary>
public sealed class MailtoRouter
{
    private readonly IComposerService _composer;
    private readonly IAccountManager? _accounts;
    private readonly IHookBus _hooks;
    private readonly IDialogService? _dialogs;
    private readonly IToastService? _toasts;
    private readonly Func<string?>? _activeAccount;
    private readonly List<MailtoFields> _pending = [];
    private readonly Lock _gate = new();

    public MailtoRouter(IComposerService composer, IAccountManager? accounts, IHookBus hooks, IDialogService? dialogs = null, IToastService? toasts = null, Func<string?>? activeAccount = null)
    {
        ArgumentNullException.ThrowIfNull(composer);
        ArgumentNullException.ThrowIfNull(hooks);
        _composer = composer;
        _accounts = accounts;
        _hooks = hooks;
        _dialogs = dialogs;
        _toasts = toasts;
        _activeAccount = activeAccount;
        if (accounts is not null)
        {
            accounts.ActiveChanged += (_, _) => _ = RetryPendingAsync(CancellationToken.None);
        }
    }

    /// <summary>Links kept while nobody is signed in (at most <see cref="MailtoLink.MaxPending"/>).</summary>
    public IReadOnlyList<MailtoFields> Pending
    {
        get
        {
            lock (_gate)
            {
                return [.. _pending];
            }
        }
    }

    /// <summary>A mailto: link to open. False when it was no link, a plugin stopped it, the user cancelled the account question, or it was kept for a sign-in.</summary>
    public async Task<bool> HandleAsync(string url, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (MailtoLink.Parse(url) is not { } link)
        {
            return false;
        }

        if (!await _hooks.AllowAsync(HookNames.OnMailtoIntercept, () => new MailtoContextPayload(url, link.To, link.Cc, link.Bcc, link.Subject, link.Body), ct).ConfigureAwait(true))
        {
            return false;
        }

        return await OpenAsync(link, ct).ConfigureAwait(true);
    }

    /// <summary>Opens the links that waited for a sign-in, one composer each (the one before is kept as a draft by the leave guard when it holds text).</summary>
    public async Task RetryPendingAsync(CancellationToken ct)
    {
        if (_accounts?.Active is null && _activeAccount?.Invoke() is null)
        {
            return;
        }

        while (true)
        {
            MailtoFields? next;
            lock (_gate)
            {
                next = _pending.Count > 0 ? _pending[0] : null;
                if (next is not null)
                {
                    _pending.RemoveAt(0);
                }
            }

            if (next is null || !await OpenAsync(next, ct).ConfigureAwait(true))
            {
                return;
            }
        }
    }

    private async Task<bool> OpenAsync(MailtoFields link, CancellationToken ct)
    {
        var account = await ChooseAccountAsync(link, ct).ConfigureAwait(true);
        if (account is null)
        {
            if (_accounts?.Accounts.Count is null or 0 && _activeAccount?.Invoke() is null)
            {
                _ = _toasts?.Show(new ToastRequest(MailtoLink.SignInMessage(link)));
                lock (_gate)
                {
                    MailtoLink.KeepPending(_pending, link);
                }
            }

            return false;
        }

        var prefill = new ComposePrefill
        {
            To = string.Join(", ", link.To),
            Cc = string.Join(", ", link.Cc),
            Bcc = string.Join(", ", link.Bcc),
            Subject = link.Subject,
            Body = link.Body,
        };
        await _composer.OpenAsync(new ComposeRequest(account, ComposeKind.New) { Prefill = prefill }, ct).ConfigureAwait(true);
        if (link.Extra.Count > 0 && _composer.IsOpen)
        {
            // the host (M6b) hands each plugin its own x-<plugin id>-<name> parameters; with no plugin host the bus has no listeners
            await _hooks.NotifyAsync(HookNames.OnComposeFromLink, new ComposeLinkContextPayload(string.Empty, link.Extra), ct).ConfigureAwait(true);
        }

        return _composer.IsOpen;
    }

    // one account: it; several: the question (the active one first); none: null
    private async Task<string?> ChooseAccountAsync(MailtoFields link, CancellationToken ct)
    {
        var list = _accounts?.Accounts ?? [];
        if (list.Count == 0)
        {
            // no signed-in login, but a mail source is active (a sample-data run): it writes the message
            return _activeAccount?.Invoke();
        }

        if (list.Count == 1 || _dialogs is null)
        {
            return _accounts!.Active?.Key ?? list[0].Key;
        }

        var detail = link.To.Count > 0 ? "To " + link.To[0] : "A new message";
        return await _dialogs.ShowAsync(new AccountPickRequest(list, _accounts!.Active?.Key, detail), ct).ConfigureAwait(true);
    }
}
