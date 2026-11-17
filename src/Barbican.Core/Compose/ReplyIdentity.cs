// Mirrors: Rust compose.rs:158 reply_identity, :246 resolve_reply_identity, :213 catch_all_identity; Bulwark lib/reply-identity.ts findReplyIdentityId, resolveReplyFrom (catch-all, subAddressDelimiter, B-COMP-014..016).

using Barbican.Core.Jmap.Submission;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Compose;

/// <summary>Reply identity resolution.</summary>
public static class ReplyIdentity
{
    /// <summary>
    /// The identity for a reply or forward, in Bulwark's order: a reopened draft's own sender; the identity that sent a self-sent original; an own identity in To/Cc/Bcc (exact address, then without the
    /// sub-address tag); with <paramref name="autoSelect"/> (not on forwards) a catch-all address on a user's domain when <paramref name="matchMode"/> is "domain" (it also rewrites From); then Delivered-To;
    /// else null (the default identity).
    /// </summary>
    public static ReplyIdentityChoice? Resolve(IReadOnlyList<Identity> identities, ReplyFrom from, bool autoSelect, string matchMode, string subAddressDelimiter = "+")
    {
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(from);
        var emails = identities.Select(i => i.Email.Trim().ToLowerInvariant()).ToList();
        if (from.DraftFrom is { } d)
        {
            // a draft keeps its sender: the identity with that address, else a From override
            var own = Find(emails, [d.Email], subAddressDelimiter);
            if (own is { } oi && string.Equals(emails[oi], d.Email.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return new ReplyIdentityChoice(oi, null);
            }

            if (d.Email.Contains('@', StringComparison.Ordinal))
            {
                var anchor = emails.FindIndex(e => string.Equals(DomainOf(e), DomainOf(d.Email), StringComparison.Ordinal));
                return new ReplyIdentityChoice(Math.Max(anchor, 0), new FromOverride(d.Name, d.Email.Trim()));
            }

            return null;
        }

        if (from.SelfSent is { } s && Find(emails, [s], subAddressDelimiter) is { } si)
        {
            return new ReplyIdentityChoice(si, null);
        }

        if (Find(emails, from.Recipients, subAddressDelimiter) is { } ri)
        {
            return new ReplyIdentityChoice(ri, null);
        }

        if (autoSelect && !from.Forward && string.Equals(matchMode, "domain", StringComparison.Ordinal))
        {
            var hit = CatchAll(emails, from.Recipients) ?? CatchAll(emails, from.Delivered);
            if (hit is { } h)
            {
                var name = from.Names.FirstOrDefault(n => string.Equals(n.Address.Trim(), h.Address, StringComparison.OrdinalIgnoreCase)).Name ?? "";
                return new ReplyIdentityChoice(h.Anchor, new FromOverride(name, h.Address));
            }
        }

        return Find(emails, from.Delivered, subAddressDelimiter) is { } di ? new ReplyIdentityChoice(di, null) : null;
    }

    private static string DomainOf(string e)
    {
        var i = e.LastIndexOf('@');
        return i < 0 ? "" : e[(i + 1)..].ToLowerInvariant();
    }

    private static string Base(string e, string delimiter)
    {
        var t = e.Trim().ToLowerInvariant();
        var at = t.LastIndexOf('@');
        if (at < 0 || delimiter.Length == 0)
        {
            return t;
        }

        var local = t[..at];
        var cut = local.IndexOf(delimiter, StringComparison.Ordinal);
        return (cut >= 0 ? local[..cut] : local) + t[at..];
    }

    /// <summary>findReplyIdentityId: an exact address first, then the address without its sub-address tag (preferring the untagged identity).</summary>
    private static int? Find(List<string> emails, IReadOnlyList<string> received, string delimiter)
    {
        foreach (var a in received)
        {
            var i = emails.IndexOf(a.Trim().ToLowerInvariant());
            if (i >= 0)
            {
                return i;
            }
        }

        foreach (var a in received)
        {
            var b = Base(a, delimiter);
            var hits = Enumerable.Range(0, emails.Count).Where(i => string.Equals(Base(emails[i], delimiter), b, StringComparison.Ordinal)).ToList();
            if (hits.Count > 0)
            {
                var untagged = hits.Where(i => string.Equals(Base(emails[i], delimiter), emails[i], StringComparison.Ordinal)).ToList();
                return untagged.Count > 0 ? untagged[0] : hits[0];
            }
        }

        return null;
    }

    private static (int Anchor, string Address)? CatchAll(List<string> emails, IReadOnlyList<string> received)
    {
        var owned = emails.Select(DomainOf).Where(d => d.Length > 0).ToList();
        foreach (var r in received)
        {
            var low = r.Trim().ToLowerInvariant();
            if (!emails.Contains(low) && owned.Contains(DomainOf(low)))
            {
                var anchor = emails.FindIndex(e => string.Equals(DomainOf(e), DomainOf(low), StringComparison.Ordinal));
                return (Math.Max(anchor, 0), r.Trim());
            }
        }

        return null;
    }
}
