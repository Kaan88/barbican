#if AUTOMATION
// Mirrors: M1-plan section 6.4 (screenshots with sample data) and M3 plan section 6.4 (a hidden automation scenario for the composer). In-memory stand-ins for send-pipeline's services, registered only in an AUTOMATION build of a
// sample-data run (BARBICAN_SAMPLE=1): they keep drafts and sent messages in the process, never reach a server, and are never part of a shipped build. The real pipeline replaces them when its steps land.

using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Editor;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Model;
using Barbican.Presentation.Mail;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.App.Views.Compose.Automation;

/// <summary>The in-memory pipeline of a sample run.</summary>
public static class SampleComposeServices
{
    public static void Register(IServiceCollection services)
    {
        var drafts = new SampleDrafts();
#if WINDOWS
        if (string.Equals(Environment.GetEnvironmentVariable("BARBICAN_SAMPLE_ADDON_ACCOUNT"), "1", StringComparison.Ordinal) &&
            string.Equals(Environment.GetEnvironmentVariable("BARBICAN_SAMPLE_SEND_PIPELINE"), "1", StringComparison.Ordinal))
        {
            bool Enabled(IServiceProvider sp) => Barbican.Core.Storage.ProfilePaths.IsAutomationProfile(sp.GetRequiredService<Barbican.Core.Storage.IProfilePaths>().Profile);
            services.Replace(ServiceDescriptor.Singleton<IComposeSeeder>(sp => Enabled(sp) ? ActivatorUtilities.CreateInstance<ComposeSeeder>(sp) : new SampleSeeder(sp.GetRequiredService<IActiveMail>())));
            services.Replace(ServiceDescriptor.Singleton<IIdentityService>(sp => Enabled(sp) ? ActivatorUtilities.CreateInstance<IdentityService>(sp) : new SampleIdentities()));
            services.Replace(ServiceDescriptor.Singleton<IDraftService>(sp => Enabled(sp) ? ActivatorUtilities.CreateInstance<DraftService>(sp) : drafts));
            services.Replace(ServiceDescriptor.Singleton<IMailSender>(sp => Enabled(sp) ? new SampleGuardedSender(ActivatorUtilities.CreateInstance<MailSender>(sp)) : new SampleSender(drafts)));
        }
        else
#endif
        {
            services.Replace(ServiceDescriptor.Singleton<IComposeSeeder>(sp => new SampleSeeder(sp.GetRequiredService<IActiveMail>())));
            services.Replace(ServiceDescriptor.Singleton<IIdentityService, SampleIdentities>());
            services.Replace(ServiceDescriptor.Singleton<IDraftService>(drafts));
            services.Replace(ServiceDescriptor.Singleton<IMailSender>(new SampleSender(drafts)));
        }
        services.Replace(ServiceDescriptor.Singleton<IAttachmentUploader, SampleUploader>());
        services.Replace(ServiceDescriptor.Singleton<ITemplateStore, SampleTemplates>());
        services.Replace(ServiceDescriptor.Singleton<IScheduledSends, SampleScheduled>());
        services.Replace(ServiceDescriptor.Singleton<Barbican.Core.Compose.Recipients.IRecipientSources, SampleRecipients>());
    }

#if WINDOWS
    private sealed class SampleGuardedSender(MailSender sender) : IMailSender
    {
        public Task<Result<SendReceipt>> SendAsync(ComposeDraft draft, SendOptions options, CancellationToken ct)
        {
            var addresses = Barbican.Core.Jmap.Submission.AddressParser.Parse(string.Join(",", draft.To, draft.Cc, draft.Bcc));
            if (addresses.Count == 0 || addresses.Any(a => !IsSynthetic(a.Email)))
                return Task.FromResult(Result<SendReceipt>.Fail(new ValidationError("Sample sends require synthetic example-domain recipients.")));
            return sender.SendAsync(draft, options, ct);
        }
        private static bool IsSynthetic(string address) => address.EndsWith("@example.org", StringComparison.OrdinalIgnoreCase) || address.EndsWith("@example.com", StringComparison.OrdinalIgnoreCase) || address.EndsWith("@example.net", StringComparison.OrdinalIgnoreCase);
        public Task<Result<ComposeSeed>> UndoAsync(SendReceipt receipt, CancellationToken ct) => sender.UndoAsync(receipt, ct);
        public Task<Result<Unit>> SendNowAsync(SendReceipt receipt, CancellationToken ct) => sender.SendNowAsync(receipt, ct);
    }
#endif

    private sealed class SampleRecipients : Barbican.Core.Compose.Recipients.IRecipientSources
    {
        public Task<Barbican.Core.Compose.Recipients.SuggestionSources> LoadAsync(string accountId, CancellationToken ct) => Task.FromResult(new Barbican.Core.Compose.Recipients.SuggestionSources
        {
            Contacts = [("Anna Berg", "anna@example.org"), ("Anders Kim", "anders@example.net"), ("Daniel Moreau", "daniel@example.com")],
            Groups = [("g1", "Project team", [Barbican.Core.Compose.Recipients.Recipient.Addr("Anna Berg", "anna@example.org"), Barbican.Core.Compose.Recipients.Recipient.Addr("Daniel Moreau", "daniel@example.com")])],
            Recent = [("", "sam@example.org"), ("Alice", "alice@example.org")],
        });
    }

    private sealed class SampleScheduled : IScheduledSends
    {
        public long MaxDelayedSend(string accountId) => 7 * 24 * 3600;

        public Task<Result<IReadOnlyList<ScheduledMessage>>> ListAsync(string accountId, CancellationToken ct) => Task.FromResult(Result<IReadOnlyList<ScheduledMessage>>.Ok([]));

        public Task<Result<Unit>> RescheduleAsync(string accountId, string submissionId, DateTimeOffset at, CancellationToken ct) => Task.FromResult(Result<Unit>.Ok(default));

        public Task<Result<string>> CancelAsync(string accountId, string submissionId, CancellationToken ct) => Task.FromResult(Result<string>.Ok("sample-email"));
    }

    private sealed class SampleSeeder(IActiveMail active) : IComposeSeeder
    {
        public async Task<Result<ComposeSeed>> SeedAsync(ComposeRequest request, CancellationToken ct)
        {
            var p = request.Prefill;
            var to = p?.To ?? string.Empty;
            var cc = p?.Cc ?? string.Empty;
            var subject = p?.Subject ?? string.Empty;
            var body = p?.Body ?? string.Empty;
            if (request.Kind is ComposeKind.Reply or ComposeKind.ReplyAll or ComposeKind.Forward && request.SourceEmailId is { } id && active.Mail is { } mail)
            {
                var original = await mail.GetBodyAsync(id, new BodyFetchOptions(), ct);
                if (original.IsOk)
                {
                    var o = original.Value!;
                    var who = o.From.Count > 0 ? o.From[0] : null;
                    var quote = (o.Preview ?? string.Empty).Replace("\n", "\n> ", StringComparison.Ordinal);
                    if (request.Kind == ComposeKind.Forward)
                    {
                        subject = "Fwd: " + o.Subject;
                        body = "\n\n---------- Forwarded message ----------\n" + (o.Preview ?? string.Empty);
                    }
                    else
                    {
                        var own = new List<string> { "sample@example.org", "work@example.org" };
                        if (active.AccountUser is { } username && string.Equals(request.AccountId, active.AccountKey, StringComparison.Ordinal)) own.Add(username);
                        var (recipients, copied) = ReplyRecipients.Build(o, request.Kind == ComposeKind.ReplyAll, own, "+");
                        cc = string.Join(", ", copied.Select(a => a.Email));
                        to = string.Join(", ", recipients.Select(a => a.Email));
                        subject = "Re: " + o.Subject;
                        body = request.InlineReply ? request.DraftText ?? string.Empty : $"{request.DraftText}\n\nOn {o.ReceivedAt:ddd d MMM}, {who?.Name ?? who?.Email} wrote:\n> {quote}";
                    }
                }
            }

            return Result<ComposeSeed>.Ok(new ComposeSeed
            {
                AccountId = request.AccountId,
                Kind = request.Kind,
                To = to,
                Cc = cc,
                Subject = subject,
                Body = Doc.FromText(body),
                SourceEmailId = request.SourceEmailId,
                DraftId = request.Kind == ComposeKind.EditDraft ? request.SourceEmailId : null,
                IsSaved = request.Kind == ComposeKind.EditDraft,
            });
        }
    }

    private sealed class SampleIdentities : IIdentityService
    {
        public Task<Result<IReadOnlyList<ComposeIdentity>>> ListAsync(string accountId, CancellationToken ct) => Task.FromResult(Result<IReadOnlyList<ComposeIdentity>>.Ok(
        [
            new ComposeIdentity(new Identity("sample-1", "Sample User", "sample@example.org"), string.Empty, string.Empty),
            new ComposeIdentity(new Identity("sample-2", "Sample Work", "work@example.org"), string.Empty, string.Empty),
        ]));
    }

    private sealed class SampleDrafts : IDraftService
    {
        private int _n;

        public List<ComposeDraft> Saved { get; } = [];

        public List<string> Discarded { get; } = [];

        public Task<Result<SavedDraft>> SaveAsync(ComposeDraft draft, CancellationToken ct)
        {
            Saved.Add(draft);
            return Task.FromResult(Result<SavedDraft>.Ok(new SavedDraft("sample-draft-" + Interlocked.Increment(ref _n))));
        }

        public Task<Result<Unit>> DiscardAsync(string accountId, string draftId, CancellationToken ct)
        {
            Discarded.Add(draftId);
            return Task.FromResult(Result<Unit>.Ok(default));
        }
    }

    private sealed class SampleSender(SampleDrafts drafts) : IMailSender
    {
        public Task<Result<SendReceipt>> SendAsync(ComposeDraft draft, SendOptions options, CancellationToken ct)
        {
            drafts.Saved.Add(draft);
            return Task.FromResult(Result<SendReceipt>.Ok(new SendReceipt(draft.AccountId, "sample-email", "sample-sub")
            {
                CanUndo = options.UndoSeconds > 0 || options.ScheduledAt is not null,
                ReleaseAt = options.ScheduledAt,
            }));
        }

        public Task<Result<ComposeSeed>> UndoAsync(SendReceipt receipt, CancellationToken ct) => Task.FromResult(Result<ComposeSeed>.Fail(new ValidationError("Undo is not simulated in a sample run.")));

        public Task<Result<Unit>> SendNowAsync(SendReceipt receipt, CancellationToken ct) => Task.FromResult(Result<Unit>.Ok(default));
    }

    private sealed class SampleUploader : IAttachmentUploader
    {
        public Task<Result<DraftAttachment>> UploadAsync(string accountId, AttachmentUpload upload, IReadOnlyList<DraftAttachment> existing, CancellationToken ct) =>
            Task.FromResult(Result<DraftAttachment>.Ok(new DraftAttachment { Name = upload.Name, Type = upload.Type, Size = upload.Data.Length, BlobId = "blob-" + upload.Name, Cid = upload.Cid }));
    }

    private sealed class SampleTemplates : ITemplateStore
    {
        private readonly List<MailTemplate> _items =
        [
            new("t1", "Meeting follow-up", "Following up on our meeting", "Hi,\n\nThanks for your time today. Here is what we agreed.\n", false) { Category = "Work" },
            new("t2", "Out of office", "Out of office", "I am away and will reply when I am back.\n", false) { Category = "Personal" },
        ];

        public Task<Result<IReadOnlyList<MailTemplate>>> ListAsync(string accountId, CancellationToken ct) => Task.FromResult(Result<IReadOnlyList<MailTemplate>>.Ok([.. _items]));

        public Task<Result<MailTemplate>> CreateAsync(string accountId, string name, string subject, string body, bool isHtml, CancellationToken ct)
        {
            var t = new MailTemplate("t" + (_items.Count + 1), name, subject, body, isHtml);
            _items.Add(t);
            return Task.FromResult(Result<MailTemplate>.Ok(t));
        }

        public Task<Result<Unit>> DeleteAsync(string accountId, string templateId, CancellationToken ct)
        {
            _ = _items.RemoveAll(t => string.Equals(t.Id, templateId, StringComparison.Ordinal));
            return Task.FromResult(Result<Unit>.Ok(default));
        }
    }
}
#endif
