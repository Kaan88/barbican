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

    /// <summary>A banner source is registered (the open waits for the banners before the page is built).</summary>
    public bool HasBanners => _sources.Count > 0;

    /// <summary>The options of one open: the account's pinned id and the mailbox roles known so far (the first open starts loading them).</summary>
    public ReaderOptions Shape(ReaderOptions options, string accountKey, IMailService mail)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(mail);
        return options with { PinnedAuthServ = pins?.PinFor(accountKey), MailboxRoles = RolesFor(accountKey, mail) };
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

    /// <summary>The banners of every source, in registration order; a failing source adds none.</summary>
    public async Task<IReadOnlyList<ReaderBanner>> BannersAsync(ReaderSnapshot snapshot, CancellationToken ct)
    {
        var all = new List<ReaderBanner>();
        foreach (var source in _sources)
        {
            try
            {
                await foreach (var banner in source.BannersForAsync(snapshot, ct).ConfigureAwait(true))
                {
                    all.Add(banner);
                }
            }
#pragma warning disable CA1031 // one broken source must not keep the message from showing
            catch (Exception) when (!ct.IsCancellationRequested)
#pragma warning restore CA1031
            {
                // nothing from this source
            }
        }

        return all;
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
}
