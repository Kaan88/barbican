// Mirrors: Rust chips.rs prefetch :370 (recent_from_sent over the newest 300 Sent messages, again after 10 minutes or for another account); Bulwark stores/contact-store.ts loadRecentRecipients; plan section 9 item 4 (contacts are M5,
// so the autocomplete uses Sent-based recents now and the contact cards later through the same interface, D-245). Owner composer-ui.

using Barbican.Core.Accounts;
using Barbican.Core.Compose.Recipients;
using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Compose;

/// <summary>The people written to, from the Sent folder of the account (cached for ten minutes).</summary>
public sealed class SentRecipientSources(IAccountManager accounts, TimeProvider time) : IRecipientSources
{
    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(10);
    private readonly Dictionary<string, (long At, SuggestionSources Value)> _cache = new(StringComparer.Ordinal);

    public async Task<SuggestionSources> LoadAsync(string accountId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        if (_cache.TryGetValue(accountId, out var hit) && time.GetElapsedTime(hit.At) < Fresh)
        {
            return hit.Value;
        }

        var mail = accounts.SessionFor(accountId)?.Mail;
        if (mail is null)
        {
            return new SuggestionSources();
        }

        var tree = await mail.GetMailboxesAsync(ct).ConfigureAwait(false);
        var sent = tree.IsOk ? tree.Value!.All.FirstOrDefault(m => m.Role == MailboxRole.Sent) : null;
        if (sent is null)
        {
            return new SuggestionSources();
        }

        var page = await mail.QueryAsync(new ListQuery { MailboxId = sent.Id, PageSize = 300 }, null, ct).ConfigureAwait(false);
        var value = new SuggestionSources { Recent = page.IsOk ? ChipSources.RecentFromSent(page.Value!.Items) : [] };
        _cache[accountId] = (time.GetTimestamp(), value);
        return value;
    }
}
