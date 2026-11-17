// Mirrors: Rust mail.rs:787 (every list delta patches the body cache: bodycache::on_sync), M1-plan §6.3 ("patched by MailSyncEvents"); the
// events come from the active account's sync engine through mailcore's MailEventHub.

using Barbican.Core.Accounts;
using Barbican.Core.Mail.Cache;
using Barbican.Presentation.Composition;
using Barbican.Presentation.Mail;

namespace Barbican.Presentation.Accounts;

/// <summary>Feeds the active account's sync events to the body cache.</summary>
public sealed class BodyCacheSync(MailEventHub hub, IBodyCache cache, IAccountManager accounts) : IStartupTask, IDisposable
{
    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        hub.EventReceived += OnEvent;
        return Task.CompletedTask;
    }

    public void Dispose() => hub.EventReceived -= OnEvent;

    private void OnEvent(object? sender, MailSyncEventArgs e)
    {
        if (accounts.Active is { } active)
        {
            cache.ApplySync(active.Key, e.Event);
        }
    }
}
