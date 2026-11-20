// Mirrors: Rust compose.rs:134-:440 open (fields, quote, identity, signature), :545 open_forward_as_attachment, :563 open_draft (inline images restored, G-058), :336 apply_signature; Bulwark components/email/email-composer.tsx, lib/reply-identity.ts, lib/reply-recipients.ts,
// lib/forward-as-attachment.ts (the message's own blob attached by id), lib/plugin-hook-catalog.ts:96-102 (onBeforeEditDraft, onBuildQuoteHeader, onBeforeComposeOpenTo*); RFC 8621 section 4.1.2.3 (messageId / inReplyTo / references), RFC 5322 section 3.6.4 (References: the parent's chain plus its Message-ID).
// Decisions D-M3A-06: a forward carries the original's non-inline attachments by blob id (no download or upload) and resends the quoted cid: images inline; a reopened draft downloads its inline images so the editor shows them (they keep their blob ids: no second upload).

using System.Net;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Editor;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Submission;
using Barbican.Core.Mail.Model;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Core.Storage;

namespace Barbican.Core.Compose;

/// <summary>Prepares a composer: recipients, subject, quoted original, identity, signature, attachments.</summary>
public sealed class ComposeSeeder(IAccountManager accounts, IIdentityService identities, IPrefsStore prefs, IHookBus hooks, IDocCodec codec, TimeProvider time) : IComposeSeeder
{
    private const int MaxRestoredImages = 24;
    private const long MaxRestoredImageBytes = 12_000_000;

    public async Task<Result<ComposeSeed>> SeedAsync(ComposeRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ctx = ComposeContext.For(accounts, request.AccountId);
        if (!ctx.IsOk)
        {
            return Result<ComposeSeed>.Fail(ctx.Error!);
        }

        var ids = await identities.ListAsync(request.AccountId, ct).ConfigureAwait(false);
        if (!ids.IsOk)
        {
            return Result<ComposeSeed>.Fail(ids.Error!);
        }

        var settings = ComposeSettings.Read(prefs.Current);
        if (request.Kind == ComposeKind.New)
        {
            return Result<ComposeSeed>.Ok(await SeedNewAsync(request, ids.Value!, ctx.Value!, settings, ct).ConfigureAwait(false));
        }

        if (request.SourceEmailId is not { Length: > 0 } sourceId)
        {
            return Result<ComposeSeed>.Fail(new ValidationError("There is no message to answer."));
        }

        var got = await ctx.Value!.Session.Mail.GetBodyAsync(sourceId, new BodyFetchOptions(), ct).ConfigureAwait(false);
        if (!got.IsOk)
        {
            return Result<ComposeSeed>.Fail(got.Error!);
        }

        var hook = request.Kind switch
        {
            ComposeKind.Reply => HookNames.OnBeforeComposeOpenToReply,
            ComposeKind.ReplyAll => HookNames.OnBeforeComposeOpenToReplyAll,
            ComposeKind.Forward => HookNames.OnBeforeComposeOpenToForward,
            ComposeKind.ForwardAsAttachment => HookNames.OnBeforeComposeOpenToForwardAsAttachment,
            _ => HookNames.OnBeforeEditDraft,
        };
        var message = await hooks.TransformAsync(hook, got.Value!, ct).ConfigureAwait(false);
        return request.Kind == ComposeKind.EditDraft
            ? await SeedDraftAsync(request, message, ids.Value!, ctx.Value, ct).ConfigureAwait(false)
            : await SeedAnswerAsync(request, message, ids.Value!, ctx.Value, settings, ct).ConfigureAwait(false);
    }

    private async Task<ComposeSeed> SeedNewAsync(ComposeRequest request, IReadOnlyList<ComposeIdentity> ids, ComposeContext ctx, ComposeSettings settings, CancellationToken ct)
    {
        var chosen = Default(ids, ctx.Username);
        var blocks = new List<Block> { Block.Para("") };
        var prefill = request.Prefill;
        if (prefill is { Body.Length: > 0 })
        {
            blocks.InsertRange(0, codec.ImportText(prefill.Body));
        }

        var (withSignature, texts) = Sign(blocks, chosen is null ? null : await ShapedAsync(ids[chosen.Value], ct).ConfigureAwait(false), settings, isNew: true);
        return new ComposeSeed
        {
            AccountId = request.AccountId,
            Kind = ComposeKind.New,
            To = prefill?.To ?? "",
            Cc = prefill?.Cc ?? "",
            Bcc = prefill?.Bcc ?? "",
            Subject = prefill?.Subject ?? "",
            Body = new Doc(withSignature),
            IdentityId = chosen is null ? null : ids[chosen.Value].Identity.Id,
            SignatureTexts = texts,
        };
    }

    private async Task<Result<ComposeSeed>> SeedAnswerAsync(ComposeRequest request, EmailBody m, IReadOnlyList<ComposeIdentity> ids, ComposeContext ctx, ComposeSettings settings, CancellationToken ct)
    {
        var forward = request.Kind is ComposeKind.Forward or ComposeKind.ForwardAsAttachment;
        var own = OwnAddresses(ids, ctx.Username);
        var choice = ReplyIdentity.Resolve([.. ids.Select(i => i.Identity)], ReplyFromOf(m, own, forward, settings), settings.AutoSelectReplyIdentity, settings.ReplyIdentityMatch, settings.SubAddressDelimiter);
        var index = choice?.Index ?? Default(ids, ctx.Username);
        // onSelectReplyIdentity (M6 set-data, a transform): a plug-in may name another of the listed identities; the pick Barbican made is then dropped with its From override
        var picked = await hooks.ShapeAsync(HookNames.OnSelectReplyIdentity, new SelectReplyIdentityPayload([.. ids.Select(x => x.Identity.Id)], index is { } pi ? ids[pi].Identity.Id : null, m.Id), ct).ConfigureAwait(false);
        if (picked.IdentityId is { } wanted && !string.Equals(wanted, index is { } ci ? ids[ci].Identity.Id : null, StringComparison.Ordinal) && ids.ToList().FindIndex(x => string.Equals(x.Identity.Id, wanted, StringComparison.Ordinal)) is >= 0 and var at)
        {
            index = at;
            choice = null;
        }

        var subject = m.Subject ?? "";
        var seed = new ComposeSeed
        {
            AccountId = request.AccountId,
            Kind = request.Kind,
            Body = new Doc([Block.Para("")]),
            SourceEmailId = m.Id,
            IdentityId = index is { } i ? ids[i].Identity.Id : null,
            FromOverride = request.InlineReply ? null : choice?.Override,
        };
        if (request.Kind == ComposeKind.ForwardAsAttachment)
        {
            return Result<ComposeSeed>.Ok(await ForwardAsAttachmentAsync(seed, m, subject, ids, index, settings, ct).ConfigureAwait(false));
        }

        if (request.InlineReply && request.Kind == ComposeKind.Reply)
        {
            var (signed, signatures) = Sign(codec.ImportText(request.DraftText ?? "").ToList(), index is { } chosen ? await ShapedAsync(ids[chosen], ct).ConfigureAwait(false) : null, settings, isNew: false);
            return Result<ComposeSeed>.Ok(ReplyEnvelope(seed, m, false, own, settings) with { Body = new Doc(signed), SignatureTexts = signatures });
        }

        var quote = QuoteOf(m);
        var from = First(m.From);
        var date = QuoteHeader.Date(m.ReceivedAt ?? m.SentAt, Barbican.Core.Settings.Pages.SettingDisplayTimeZone.Resolve(prefs.Current.Str("timeZone", "auto"), time.LocalTimeZone));
        var who = QuoteHeader.Who(from);
        var inline = CidHtml.InlineParts(m, quote.Html);
        List<Block> blocks;
        if (forward)
        {
            (seed, blocks) = ForwardOf(seed, m, quote, inline, who, date);
        }
        else
        {
            var header = await HeaderAsync(request.Kind, who, date, from, m, subject, ct).ConfigureAwait(false);
            (seed, blocks) = ReplyOf(seed, m, quote, inline, header, request.Kind == ComposeKind.ReplyAll, own, settings);
            if (request.Kind is ComposeKind.Reply or ComposeKind.ReplyAll && request.DraftText is { Length: > 0 } draftText)
            {
                blocks.InsertRange(0, codec.ImportText(draftText));
            }
        }

        var (body, texts) = Sign(blocks, index is { } si ? await ShapedAsync(ids[si], ct).ConfigureAwait(false) : null, settings, isNew: false);
        return Result<ComposeSeed>.Ok(seed with { Body = new Doc(body), SignatureTexts = texts });
    }

    private static ReplyFrom ReplyFromOf(EmailBody m, List<string> own, bool forward, ComposeSettings settings)
    {
        var from = First(m.From);
        var all = Or(m.To).Concat(Or(m.Cc)).Concat(Or(m.Bcc)).ToList();
        return new ReplyFrom
        {
            SelfSent = from is not null && ReplyRecipients.IsOwn(from.Email, own, settings.SubAddressDelimiter) ? from.Email : null,
            Recipients = [.. all.Select(a => a.Email)],
            Delivered = ComposeRules.DeliveryAddresses(Or(m.Headers)),
            Forward = forward,
            Names = [.. all.Select(a => (a.Email.Trim().ToLowerInvariant(), (a.Name ?? "").Trim()))],
        };
    }

    private static (ComposeSeed Seed, List<Block> Blocks) ForwardOf(ComposeSeed seed, EmailBody m, (string Html, string Text) quote, IReadOnlyList<(string Cid, DraftAttachment Part)> inline, string who, string date)
    {
        var subject = m.Subject ?? "";
        var blocks = new List<Block>
        {
            Block.Para(""),
            MarkComposerContext(Block.Para(QuoteHeader.ForwardedBanner)),
            MarkComposerContext(Block.Para($"From: {who}\nDate: {date}\nSubject: {subject}")),
            new Block(new BlockKind.Quoted(quote.Html, quote.Text, true, false)),
        };
        var inlineBlobs = inline.Select(p => p.Part.BlobId).ToHashSet(StringComparer.Ordinal);
        return (seed with
        {
            Subject = Prefixed(subject, "Fwd:"),
            Attachments =
            [
                .. Or(m.Attachments).Where(a => a.BlobId is not null && !inlineBlobs.Contains(a.BlobId)).Select(a => new DraftAttachment { Name = a.Name ?? "attachment", Type = a.Type ?? "application/octet-stream", Size = a.Size, BlobId = a.BlobId }),
                .. inline.Select(p => p.Part),
            ],
        }, blocks);
    }

    private static (ComposeSeed Seed, List<Block> Blocks) ReplyOf(ComposeSeed seed, EmailBody m, (string Html, string Text) quote, IReadOnlyList<(string Cid, DraftAttachment Part)> inline, string header, bool all, List<string> own, ComposeSettings settings)
    {
        var blocks = new List<Block> { Block.Para(""), MarkComposerContext(Block.Para(header)), new Block(new BlockKind.Quoted(quote.Html, quote.Text, false, true)) };
        var reply = ReplyEnvelope(seed, m, all, own, settings);
        return (reply with { Attachments = [.. inline.Select(p => p.Part)] }, blocks);
    }

    private static ComposeSeed ReplyEnvelope(ComposeSeed seed, EmailBody m, bool all, List<string> own, ComposeSettings settings)
    {
        var (to, cc) = ReplyRecipients.Build(m, all, own, settings.SubAddressDelimiter);
        var msgId = First(m.MessageId);
        return seed with
        {
            To = string.Join(", ", to.Select(a => QuoteHeader.FormatRecipient(a.Name, a.Email))),
            Cc = string.Join(", ", cc.Select(a => QuoteHeader.FormatRecipient(a.Name, a.Email))),
            Subject = Prefixed(m.Subject ?? "", "Re:"),
            InReplyTo = msgId,
            References = [.. Or(m.References), .. msgId is null ? [] : new[] { msgId }],
        };
    }

    private async Task<ComposeSeed> ForwardAsAttachmentAsync(ComposeSeed seed, EmailBody m, string subject, IReadOnlyList<ComposeIdentity> ids, int? index, ComposeSettings settings, CancellationToken ct)
    {
        // Bulwark lib/forward-as-attachment.ts: the message's own blob is attached by id, the body starts empty
        var blob = m.Extra is { } e && e.TryGetValue("blobId", out var b) && b.ValueKind == System.Text.Json.JsonValueKind.String ? b.GetString() : null;
        var (body, texts) = Sign([Block.Para("")], index is { } si ? await ShapedAsync(ids[si], ct).ConfigureAwait(false) : null, settings, isNew: false);
        var name = Sanitize(subject.Length == 0 ? "message" : subject) + ".eml";
        return seed with
        {
            Subject = Prefixed(subject, "Fwd:"),
            Body = new Doc(body),
            SignatureTexts = texts,
            Attachments = blob is null ? [] : [new DraftAttachment { Name = name, Type = "message/rfc822", Size = m.Size, BlobId = blob }],
        };
    }

    private async Task<string> HeaderAsync(ComposeKind kind, string who, string date, EmailAddress? from, EmailBody m, string subject, CancellationToken ct)
    {
        var header = QuoteHeader.Reply(date, who);
        if (!hooks.HasHandlers(HookNames.OnBuildQuoteHeader))
        {
            return header;
        }

        var payload = new QuoteHeaderPayload(header, kind.ToString(), from?.Name ?? "", from?.Email ?? "", m.SentAt ?? m.ReceivedAt, subject);
        return (await hooks.TransformAsync(HookNames.OnBuildQuoteHeader, payload, ct).ConfigureAwait(false)).Header;
    }

    private async Task<Result<ComposeSeed>> SeedDraftAsync(ComposeRequest request, EmailBody m, IReadOnlyList<ComposeIdentity> ids, ComposeContext ctx, CancellationToken ct)
    {
        var from = First(m.From);
        var settings = ComposeSettings.Read(prefs.Current);
        var emails = ids.Select(i => i.Identity).ToList();
        var choice = ReplyIdentity.Resolve(emails, new ReplyFrom { DraftFrom = from is null ? null : new FromOverride(from.Name ?? "", from.Email) }, false, "exact", settings.SubAddressDelimiter);
        var html = string.Join('\n', Or(m.HtmlBody).Select(p => p.PartId is { } id && Or(m.BodyValues).TryGetValue(id, out var v) ? v.Value : "")).Trim();
        var blocks = html.Length == 0 ? codec.ImportText(TextOf(m)) : codec.ImportWithQuotes(codec.CleanQuotedHtml(html));

        // inline images stay in the body (referenced by cid), not in the attachment list
        bool IsInline(Attachment a) => a.Cid is { } c && html.Contains("cid:" + c.Trim('<', '>'), StringComparison.Ordinal);
        var attachments = Or(m.Attachments).Where(a => a.BlobId is not null).Select(a => new DraftAttachment
        {
            Name = a.Name ?? "attachment",
            Type = a.Type ?? "application/octet-stream",
            Size = a.Size,
            BlobId = a.BlobId,
            Cid = IsInline(a) ? a.Cid!.Trim('<', '>') : null,
        }).ToList();
        await RestoreImagesAsync(blocks, attachments, ctx, ct).ConfigureAwait(false);
        // Stored MIME can flatten the reply attribution and signature into normal text.
        // Treat imported runs as un-attributed until the user edits the re-opened composer.
        foreach (var block in blocks) MarkComposerContext(block);
        return Result<ComposeSeed>.Ok(new ComposeSeed
        {
            AccountId = request.AccountId,
            Kind = ComposeKind.EditDraft,
            To = Join(Or(m.To)),
            Cc = Join(Or(m.Cc)),
            Bcc = Join(Or(m.Bcc)),
            Subject = m.Subject ?? "",
            Body = new Doc(blocks),
            IdentityId = choice is null ? Default(ids, ctx.Username) is { } d ? ids[d].Identity.Id : null : ids[choice.Index].Identity.Id,
            FromOverride = choice?.Override,
            Attachments = attachments,
            DraftId = m.Id,
            InReplyTo = First(m.InReplyTo),
            References = Or(m.References),
            Keywords = OutgoingKeywords.Clean(m.Keywords.Map.Where(keyword => keyword.Value).Select(keyword => keyword.Key)),
            IsSaved = true,
        });
    }

    /// <summary>G-058: an inline image of a reopened draft keeps its blob id and gets its bytes back, so the editor shows it and it is never uploaded a second time.</summary>
    private static async Task RestoreImagesAsync(IReadOnlyList<Block> blocks, IReadOnlyList<DraftAttachment> attachments, ComposeContext ctx, CancellationToken ct)
    {
        var byCid = attachments.Where(a => a.Cid is not null).ToDictionary(a => a.Cid!, a => a, StringComparer.Ordinal);
        var restored = 0;
        foreach (var block in blocks)
        {
            if (block.Kind is not BlockKind.Image { Data: { Cid: { } cid } data } || !byCid.TryGetValue(cid.Trim('<', '>'), out var part) || restored++ >= MaxRestoredImages)
            {
                continue;
            }

            byte[]? bytes = null;
            if (part.BlobId is { } blob && part.Size <= MaxRestoredImageBytes)
            {
                var stream = await ctx.Client.DownloadAsync(ctx.Account, blob, part.Name, part.Type, ct).ConfigureAwait(false);
                if (stream.IsOk)
                {
                    using var ms = new MemoryStream();
                    await stream.Value!.CopyToAsync(ms, ct).ConfigureAwait(false);
                    await stream.Value.DisposeAsync().ConfigureAwait(false);
                    bytes = ms.ToArray();
                }
            }

            block.Kind = new BlockKind.Image(data with { Cid = cid.Trim('<', '>'), BlobId = part.BlobId, Mime = part.Type, Name = part.Name, Bytes = bytes ?? data.Bytes });
        }
    }

    private (string Html, string Text) QuoteOf(EmailBody m)
    {
        var text = TextOf(m);
        var html = CidHtml.Of(m);
        if (html.Trim().Length > 0)
        {
            var cleaned = codec.CleanQuotedHtml(html);
            return (cleaned, text.Trim().Length == 0 ? codec.HtmlText(cleaned) : text);
        }

        var lines = text.Split('\n').Select(l => "<div>" + (l.Length == 0 ? "<br>" : WebUtility.HtmlEncode(l)) + "</div>");
        return (string.Concat(lines), text);
    }

    private string TextOf(EmailBody m)
    {
        var text = string.Join('\n', Or(m.TextBody).Select(p => p.PartId is { } id && Or(m.BodyValues).TryGetValue(id, out var v) ? v.Value : ""));
        if (text.Trim().Length > 0)
        {
            return text;
        }

        var html = string.Join('\n', Or(m.HtmlBody).Select(p => p.PartId is { } id && Or(m.BodyValues).TryGetValue(id, out var v) ? v.Value : ""));
        return html.Trim().Length == 0 ? "" : codec.HtmlText(html);
    }

    /// <summary>onSignatureRender (M6 set-data, a transform): a plug-in may replace the signature that is about to be inserted; the identity as it is when nothing listens.</summary>
    private async Task<ComposeIdentity> ShapedAsync(ComposeIdentity identity, CancellationToken ct)
    {
        if (!hooks.HasHandlers(HookNames.OnSignatureRender))
        {
            return identity;
        }

        var shaped = await hooks.TransformAsync(HookNames.OnSignatureRender, new SignatureRenderPayload(identity.HtmlSignature, identity.TextSignature, identity.Identity.Id), ct).ConfigureAwait(false);
        return identity with { HtmlSignature = shaped.Html, TextSignature = shaped.Text };
    }

    /// <summary>Rust apply_signature: the separator and the identity's signature (HTML when it has text, else its text), below the quote or above it, into the document.</summary>
    private (List<Block> Blocks, IReadOnlyList<string> Texts) Sign(List<Block> blocks, ComposeIdentity? identity, ComposeSettings settings, bool isNew)
    {
        if (identity is null || !(isNew ? settings.SignatureNewMail : settings.SignatureReplyForward))
        {
            return (blocks, []);
        }

        var htmlText = identity.HtmlSignature.Trim().Length > 0 ? codec.HtmlText(identity.HtmlSignature) : "";
        if (identity.TextSignature.Trim().Length == 0 && htmlText.Trim().Length == 0)
        {
            return (blocks, []);
        }

        var sig = new List<Block>();
        if (settings.SignatureSeparator)
        {
            sig.Add(Block.Para("-- "));
        }

        sig.AddRange(htmlText.Trim().Length > 0 ? codec.Import(identity.HtmlSignature) : codec.ImportText(identity.TextSignature.TrimEnd()));
        foreach (var block in sig) MarkComposerContext(block);
        var quoted = blocks.FindIndex(b => b.Kind is BlockKind.Quoted);
        int at;
        if (quoted >= 0 && string.Equals(settings.SignaturePosition, "above_quote", StringComparison.Ordinal))
        {
            // above the quote and its "wrote:" line
            at = quoted > 0 && !blocks[quoted - 1].IsEmpty ? quoted - 1 : quoted;
            sig.Add(Block.Para(""));
        }
        else
        {
            sig.Insert(0, Block.Para(""));
            at = blocks.Count;
        }

        blocks.InsertRange(Math.Min(at, blocks.Count), sig);
        return (blocks, [(htmlText.Trim().Length > 0 ? htmlText : identity.TextSignature).Trim()]);
    }

    private static Block MarkComposerContext(Block block)
    {
        for (var i = 0; i < block.Runs.Count; i++) block.Runs[i] = block.Runs[i] with { IsComposerContext = true };
        return block;
    }

    private static IReadOnlyList<T> Or<T>(IReadOnlyList<T>? list) => list ?? [];

    private static IReadOnlyDictionary<string, V> Or<V>(IReadOnlyDictionary<string, V>? map) => map ?? new Dictionary<string, V>(StringComparer.Ordinal);

    private static T? First<T>(IReadOnlyList<T>? list)
        where T : class => list is { Count: > 0 } ? list[0] : null;

    private static int? Default(IReadOnlyList<ComposeIdentity> ids, string username)
    {
        if (ids.Count == 0)
        {
            return null;
        }

        var me = ids.ToList().FindIndex(i => string.Equals(i.Identity.Email.Trim(), username, StringComparison.OrdinalIgnoreCase));
        return me >= 0 ? me : 0;
    }

    private static List<string> OwnAddresses(IReadOnlyList<ComposeIdentity> ids, string username) =>
        [.. ids.Select(i => i.Identity.Email.Trim()).Append(username).Where(e => e.Contains('@', StringComparison.Ordinal)).Distinct(StringComparer.OrdinalIgnoreCase)];

    private static string Prefixed(string subject, string prefix) => subject.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? subject : $"{prefix} {subject}";

    private static string Join(IReadOnlyList<EmailAddress> list) => string.Join(", ", list.Select(a => QuoteHeader.FormatRecipient(a.Name, a.Email)));

    private static string Sanitize(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var cleaned = new string([.. name.Select(c => bad.Contains(c) || char.IsControl(c) ? '_' : c)]).Trim();
        return cleaned.Length == 0 ? "message" : cleaned.Length > 100 ? cleaned[..100] : cleaned;
    }
}
