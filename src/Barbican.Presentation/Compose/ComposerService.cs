// Mirrors: Rust compose.rs open :134 (the hooks, the seed, the replace question), should_dock :418; Bulwark components/email/email-composer.tsx (the plugin composer hooks), reply-dock.tsx; M3 plan sections 4.4 and 6.4 step 2.
// The one composer of the window: opens it from a request or a seed (the plugin hooks first, the leave guard for the one it replaces), ticks its autosave every 500 ms, and closes it. Owner composer-ui.

using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Presentation.Actions;

namespace Barbican.Presentation.Compose;

/// <summary>The default and only implementation of <see cref="IComposerService"/>.</summary>
public sealed class ComposerService : IComposerService, IDisposable
{
    private readonly ComposerServices _s;
    private readonly SemaphoreSlim _opening = new(1, 1);
    private ComposerViewModel? _current;
    private ITimer? _timer;

    public ComposerService(ComposerServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _s = services;
    }

    /// <inheritdoc />
    public event EventHandler? OpenChanged;

    /// <inheritdoc />
    public bool IsOpen => _current is not null;

    /// <inheritdoc />
    public ComposerViewModel? Current => _current;

    /// <inheritdoc />
    public async Task OpenAsync(ComposeRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!await AllowAsync(request, ct).ConfigureAwait(true) || !await TryCloseAsync(ct).ConfigureAwait(true))
        {
            return;
        }

        ComposeSeed seed;
        try
        {
            var res = await _s.Seeder.SeedAsync(request, ct).ConfigureAwait(true);
            if (!res.IsOk)
            {
                _ = _s.Toasts?.Show(new ToastRequest("Could not open the composer: " + res.Error!.Message, IsError: true));
                return;
            }

            seed = res.Value!;
        }
        catch (PortPendingException)
        {
            _ = _s.Toasts?.Show(new ToastRequest("Writing messages is not available in this build yet.", IsError: true));
            return;
        }

        await OpenSeedCoreAsync(seed, request.Kind, ct).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async Task OpenSeedAsync(ComposeSeed seed, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(seed);
        if (await TryCloseAsync(ct).ConfigureAwait(true))
        {
            await OpenSeedCoreAsync(seed, seed.Kind, ct).ConfigureAwait(true);
        }
    }

    /// <inheritdoc />
    public async Task<bool> TryCloseAsync(CancellationToken ct) => _current is not { } c || await c.RequestCloseAsync().ConfigureAwait(true);

    /// <inheritdoc />
    public async Task FlushAsync(CancellationToken ct)
    {
        if (_current is { } c)
        {
            await c.FlushAsync().ConfigureAwait(true);
        }
    }

    // the interceptors of Bulwark's catalogue: onBeforeReply / ReplyAll / Forward for the three answers, onBeforeCompose for everything (a handler returning false stops the composer)
    private async Task<bool> AllowAsync(ComposeRequest request, CancellationToken ct)
    {
        var hook = request.Kind switch
        {
            ComposeKind.Reply => HookNames.OnBeforeReply,
            ComposeKind.ReplyAll => HookNames.OnBeforeReplyAll,
            ComposeKind.Forward or ComposeKind.ForwardAsAttachment => HookNames.OnBeforeForward,
            _ => null,
        };
        if (hook is not null && request.SourceEmailId is { } source && !await _s.Hooks.AllowAsync(hook, () => new ReplyContextPayload(request.AccountId, source), ct).ConfigureAwait(true))
        {
            return false;
        }

        var p = request.Prefill;
        return await _s.Hooks.AllowAsync(
            HookNames.OnBeforeCompose,
            () => new ComposeOptionsPayload(request.AccountId, request.Kind.ToString(), p?.To ?? string.Empty, p?.Cc ?? string.Empty, p?.Bcc ?? string.Empty, p?.Subject ?? string.Empty, p?.Body ?? string.Empty),
            ct).ConfigureAwait(true);
    }

    private async Task OpenSeedCoreAsync(ComposeSeed seed, ComposeKind kind, CancellationToken ct)
    {
        await _opening.WaitAsync(ct).ConfigureAwait(true);
        try
        {
            var settings = ComposeSettings.Read(_s.Prefs.Current);
            IReadOnlyList<ComposeIdentity> identities = [];
            try
            {
                var ids = await _s.Identities.ListAsync(seed.AccountId, ct).ConfigureAwait(true);
                identities = ids.IsOk ? ids.Value! : [];
            }
            catch (PortPendingException)
            {
                // no identity service yet: the From select stays empty and a send says so
            }

            var context = new ComposerContext(_s.Drafts, _s.Uploader, _s.Sender, _s.Editors, _s.Sources, _s.Hooks, _s.Time, settings)
            {
                Toasts = _s.Toasts,
                Templates = _s.Templates,
                TemplatePickers = _s.TemplatePickers,
                Reopen = OpenSeedAsync,
                MentionsEnabled = _s.Prefs.Current.Bool("recipientMentionsEnabled", true),
                MentionAddsRecipientTo = _s.Prefs.Current.Str("mentionAddsRecipientTo", "to"),
                ConfirmExternalRecipients = _s.Prefs.Current.Bool("externalRecipientConfirm", false),
            };
            var composer = new ComposerViewModel(context, seed, identities)
            {
                IsDocked = ComposerDock.ShouldDock(settings.ReplyUnderMessage, kind == ComposeKind.New, _s.MessageOpen(), kind == ComposeKind.EditDraft),
            };
            composer.Closed += OnClosed;
            _current = composer;
            _timer = _s.Time.CreateTimer(_ => _s.Dispatcher.Post(composer.Tick), null, AutosaveState.Tick, AutosaveState.Tick);
            OpenChanged?.Invoke(this, EventArgs.Empty);
            _s.Hooks.Fire(HookNames.OnComposerOpen, () => new ComposerContextPayload(composer.Id, composer.AccountKey, kind.ToString(), composer.DraftId));
            _ = LoadSourcesAsync(composer);
        }
        finally
        {
            _ = _opening.Release();
        }
    }

    private async Task LoadSourcesAsync(ComposerViewModel composer)
    {
        try
        {
            composer.SetSources(await _s.Sources.LoadAsync(composer.AccountKey, CancellationToken.None).ConfigureAwait(true));
        }
        catch (Exception e) when (e is PortPendingException or HttpRequestException or IOException)
        {
            // no suggestions: typing still works
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _opening.Dispose();
    }

    private void OnClosed(object? sender, ComposerClosedEventArgs e)
    {
        if (sender is not ComposerViewModel composer || !ReferenceEquals(composer, _current))
        {
            return;
        }

        _timer?.Dispose();
        _timer = null;
        _current = null;
        composer.Dispose();
        OpenChanged?.Invoke(this, EventArgs.Empty);
        _s.Hooks.Fire(HookNames.OnComposerClose, () => new ComposerClosedPayload(e.DraftId, e.Sent));
    }
}
