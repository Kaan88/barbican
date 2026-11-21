// Mirrors: M2 plan §6.3 steps 5 and 10: R2-07 (authserv-id learned and pinned per account; no verdict in Sent and Drafts), trusted senders
// (I-209: their remote content loads), the banner stack from IReaderBannerSource set with the snapshot so it shows in the same UI turn as the
// reveal (PORT-PLAN §4.5). Bulwark components/email/email-viewer.tsx (banners above the body), lib/server-auth-status.ts. Owner reader.

using Barbican.Core.Mail;
using Barbican.Core.Mail.AuthResults;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Trust;
using Barbican.Presentation.Reader;

namespace Barbican.Presentation.Mail.Loading;

/// <summary>What the M2 reader adds to an open: the pinned authserv-id, mailbox roles, trusted senders and the banners.</summary>
public sealed class ReaderDepth(AuthServPins? pins, IEnumerable<IReaderBannerSource> sources, ITrustedSenders? trusted)
{
    private readonly IReadOnlyList<IReaderBannerSource> _sources = [.. sources];
    private readonly Lock _lock = new();
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _roles = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loading = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<string>> _domains = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loadingDomains = new(StringComparer.Ordinal);

    /// <summary>A banner source is registered (the open waits for the banners before the page is built).</summary>
    public bool HasBanners => _sources.Count > 0;

    /// <summary>The options of one open: the account's pinned id and the mailbox roles known so far (the first open starts loading them).</summary>
    /// <param name="options">The options so far.</param>
    /// <param name="accountKey">The login that holds the message (its pin).</param>
    /// <param name="mail">The read service of the account that holds it.</param>
    /// <param name="rolesKey">The key its folder roles are kept under (a shared account's own, Fable review 3 R3-01); the login when null.</param>
    public ReaderOptions Shape(ReaderOptions options, string accountKey, IMailService mail, string? rolesKey = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(mail);
        return options with
        {
            PinnedAuthServ = pins?.PinFor(accountKey),
            MailboxRoles = RolesFor(rolesKey ?? accountKey, mail),
            InternalDomains = DomainsFor(accountKey, mail),
        };
    }

    /// <summary>The account's identity domains, already read at sign-in (MailShellStartup), so the first message's chip is decided before it opens.</summary>
    public void SetIdentities(string accountKey, IEnumerable<string> emails)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        var domains = ExternalSenderRules.InternalDomains(emails);
        lock (_lock)
        {
            _domains[accountKey] = domains;
        }
    }

    /// <summary>The identity domains known for the account; an unknown one starts loading them, and its messages get the chip from the next open on.</summary>
    public IReadOnlyList<string> DomainsFor(string accountKey, IMailService mail)
    {
        lock (_lock)
        {
            if (_domains.TryGetValue(accountKey, out var known))
            {
                return known;
            }

            if (_loadingDomains.Add(accountKey))
            {
                _ = LoadDomainsAsync(accountKey, mail);
            }

            return [];
        }
    }

    /// <summary>Pins the id the server's trace block was found under, the first time one is seen for the account.</summary>
    public void Learn(string accountKey, string? learned)
    {
        if (learned is not null)
        {
            pins?.Learn(accountKey, learned);
        }
    }

    /// <summary>Whether the sender's remote content loads without asking.</summary>
    public bool Trusted(EmailBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return trusted is not null && body.From.Count > 0 && trusted.IsTrusted(body.From[0].Email);
    }

    /// <summary>How long an open waits for the banners before the page is built: nobody waits on a slow server (M2 plan §1 rule 8); the
    /// banners of a slower source join the stack afterwards, moving the body down once by their own height (M2 plan §6.3 step 10).</summary>
    public static readonly TimeSpan BannerBudget = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// The banners of every source that answered within <see cref="BannerBudget"/>, in registration order. <c>Late</c> is null when every
    /// source answered in time, else it completes with the banners of the slower ones (registration order). A failing or cancelled source adds
    /// none.
    /// </summary>
    public async Task<(IReadOnlyList<ReaderBanner> Ready, Task<IReadOnlyList<ReaderBanner>>? Late)> BannersAsync(ReaderSnapshot snapshot, TimeProvider time, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(time);
        List<Task<List<ReaderBanner>>> tasks = [.. _sources.Select(s => CollectAsync(s, snapshot, ct))];
        var all = Task.WhenAll(tasks);
        if (!all.IsCompleted)
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            await Task.WhenAny(all, Task.Delay(BannerBudget, time, stop.Token)).ConfigureAwait(true);
            await stop.CancelAsync().ConfigureAwait(true);
        }

        var done = tasks.Select(t => t.IsCompleted).ToArray();
        var ready = new List<ReaderBanner>();
        for (var i = 0; i < tasks.Count; i++)
        {
            if (done[i])
            {
#pragma warning disable VSTHRD003 // started above by this method (CollectAsync) and already complete; it never faults
                ready.AddRange(await tasks[i].ConfigureAwait(true));
#pragma warning restore VSTHRD003
            }
        }

        return (ready, done.All(d => d) ? null : LateAsync(tasks, done));
    }

    private static async Task<IReadOnlyList<ReaderBanner>> LateAsync(List<Task<List<ReaderBanner>>> tasks, bool[] done)
    {
        var late = new List<ReaderBanner>();
        for (var i = 0; i < tasks.Count; i++)
        {
            if (!done[i])
            {
#pragma warning disable VSTHRD003 // started by BannersAsync (CollectAsync), no context needed; it never faults
                late.AddRange(await tasks[i].ConfigureAwait(false));
#pragma warning restore VSTHRD003
            }
        }

        return late;
    }

    // One source's banners; it never faults (a broken or cancelled source adds none), so the late task never goes unobserved.
    private static async Task<List<ReaderBanner>> CollectAsync(IReaderBannerSource source, ReaderSnapshot snapshot, CancellationToken ct)
    {
        var list = new List<ReaderBanner>();
        try
        {
            await foreach (var banner in source.BannersForAsync(snapshot, ct).ConfigureAwait(true))
            {
                list.Add(banner);
            }
        }
#pragma warning disable CA1031 // one broken source must not keep the message from showing
        catch (Exception)
#pragma warning restore CA1031
        {
            return [];
        }

        return list;
    }

    private IReadOnlyDictionary<string, string> RolesFor(string accountKey, IMailService mail)
    {
        lock (_lock)
        {
            if (_roles.TryGetValue(accountKey, out var known))
            {
                return known;
            }

            if (!_loading.Add(accountKey))
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }

        _ = LoadRolesAsync(accountKey, mail);
        return new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private async Task LoadRolesAsync(string accountKey, IMailService mail)
    {
        var tree = await mail.GetMailboxesAsync(CancellationToken.None).ConfigureAwait(false);
        lock (_lock)
        {
            _loading.Remove(accountKey);
            if (tree.IsOk)
            {
                _roles[accountKey] = tree.Value!.All.Where(m => m.Role != MailboxRole.None).ToDictionary(m => m.Id, m => m.Role.ToString().ToLowerInvariant(), StringComparer.Ordinal);
            }
        }
    }

    private async Task LoadDomainsAsync(string accountKey, IMailService mail)
    {
        IReadOnlyList<string>? domains = null;
        try
        {
            if (await mail.GetIdentitiesAsync(CancellationToken.None).ConfigureAwait(false) is { IsOk: true } ids)
            {
                domains = ExternalSenderRules.InternalDomains(ids.Value!.Select(i => i.Email));
            }
        }
#pragma warning disable CA1031 // a server that cannot list its identities leaves nobody internal, and the next open asks again
        catch (Exception)
#pragma warning restore CA1031
        {
        }

        lock (_lock)
        {
            _loadingDomains.Remove(accountKey);
            if (domains is not null)
            {
                _domains[accountKey] = domains;
            }
        }
    }
}
