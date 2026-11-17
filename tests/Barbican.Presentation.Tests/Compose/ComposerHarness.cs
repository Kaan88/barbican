// The fakes of the composer tests: the pipeline services (send-pipeline's interfaces), the editor factory (not registered yet: the composer falls back to its plain text stand-in), the toasts and a manual clock.
// Owner composer-ui.

using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Editor;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Model;
using Barbican.Core.Plugins.Hooks;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Editor;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Presentation.Tests.Compose;

internal sealed class ComposerHarness
{
    public ComposerHarness(ComposeSettings? settings = null, HookBus? hooks = null)
    {
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero));
        Context = new ComposerContext(Drafts, Uploader, Sender, new NoEditor(), new Barbican.Core.Compose.Recipients.EmptyRecipientSources(), hooks ?? new HookBus(), Time, settings ?? new ComposeSettings())
        {
            Toasts = Toasts,
            Templates = Store,
        };
    }

    public FakeTimeProvider Time { get; }

    public FakeDrafts Drafts { get; } = new();

    public FakeUploader Uploader { get; } = new();

    public FakeSender Sender { get; } = new();

    public FakeToasts Toasts { get; } = new();

    public FakeTemplates Store { get; } = new();

    public ComposerContext Context { get; }

    public static IReadOnlyList<ComposeIdentity> Identities() =>
    [
        new(new Identity("i1", "Me", "me@home.example"), string.Empty, string.Empty),
        new(new Identity("i2", "Work", "me@work.example"), string.Empty, string.Empty),
    ];

    public ComposerViewModel Open(ComposeSeed? seed = null) => new(Context, seed ?? Seed(), Identities());

    public static ComposeSeed Seed(string body = "", ComposeKind kind = ComposeKind.New, string to = "", string subject = "", string? draftId = null, bool saved = false) => new()
    {
        AccountId = "acc",
        Kind = kind,
        To = to,
        Subject = subject,
        Body = Doc.FromText(body),
        DraftId = draftId,
        IsSaved = saved,
    };

    /// <summary>Runs ticks until the composer was quiet long enough for an autosave.</summary>
    public void Quiet(ComposerViewModel composer)
    {
        composer.Tick();
        Time.Advance(AutosaveState.Quiet + TimeSpan.FromMilliseconds(100));
        composer.Tick();
    }

    internal sealed class NoEditor : IEditorViewModelFactory
    {
        public IEditorViewModel Create(Doc doc, bool plain) => throw new PortPendingException("test: no editor");
    }

    internal sealed class FakeDrafts : IDraftService
    {
        public List<ComposeDraft> Saved { get; } = [];

        public List<string> Discarded { get; } = [];

        public bool Fail { get; set; }

        public Task<Result<SavedDraft>> SaveAsync(ComposeDraft draft, CancellationToken ct)
        {
            Saved.Add(draft);
            return Task.FromResult(Fail ? Result<SavedDraft>.Fail(new ValidationError("nope")) : Result<SavedDraft>.Ok(new SavedDraft("d" + Saved.Count)));
        }

        public Task<Result<Unit>> DiscardAsync(string accountId, string draftId, CancellationToken ct)
        {
            Discarded.Add(draftId);
            return Task.FromResult(Result<Unit>.Ok(default));
        }
    }

    internal sealed class FakeUploader : IAttachmentUploader
    {
        public TaskCompletionSource<Result<DraftAttachment>>? Hold { get; set; }

        public Task<Result<DraftAttachment>> UploadAsync(string accountId, AttachmentUpload upload, IReadOnlyList<DraftAttachment> existing, CancellationToken ct)
        {
            if (Hold is { } h)
            {
                return h.Task.WaitAsync(ct);
            }

            return Task.FromResult(Result<DraftAttachment>.Ok(new DraftAttachment { Name = upload.Name, Type = upload.Type, Size = upload.Data.Length, BlobId = "b-" + upload.Name }));
        }
    }

    internal sealed class FakeSender : IComposerSender
    {
        public List<(ComposeDraft Draft, SendOptions Options)> Sent { get; } = [];

        public bool Fail { get; set; }

        public bool CanUndo { get; set; }

        public long MaxDelayedSeconds(string accountKey) => 7 * 24 * 3600;

        public Task<Result<SendReceipt>> SendAsync(ComposeDraft draft, SendOptions options, CancellationToken ct)
        {
            Sent.Add((draft, options));
            return Task.FromResult(Fail ? Result<SendReceipt>.Fail(new ValidationError("rejected")) : Result<SendReceipt>.Ok(new SendReceipt("acc", "e1", "s1") { CanUndo = CanUndo }));
        }

        public Task<Result<ComposeSeed>> UndoAsync(SendReceipt receipt, CancellationToken ct) => Task.FromResult(Result<ComposeSeed>.Ok(Seed("back")));
    }

    internal sealed class FakeToasts : IToastService
    {
        public List<ToastRequest> Shown { get; } = [];

        public ToastHandle Show(ToastRequest toast)
        {
            Shown.Add(toast);
            return default;
        }

        public void Dismiss(ToastHandle handle)
        {
        }
    }

    internal sealed class FakeTemplates : ITemplateStore
    {
        public List<MailTemplate> Items { get; } = [new("t1", "Welcome", "Hello there", "Dear customer", false) { Category = "Sales" }, new("t2", "Invoice", "Your invoice", "See attached", false)];

        public Task<Result<IReadOnlyList<MailTemplate>>> ListAsync(string accountId, CancellationToken ct) => Task.FromResult(Result<IReadOnlyList<MailTemplate>>.Ok([.. Items]));

        public Task<Result<MailTemplate>> CreateAsync(string accountId, string name, string subject, string body, bool isHtml, CancellationToken ct)
        {
            var t = new MailTemplate("t" + (Items.Count + 1), name, subject, body, isHtml);
            Items.Add(t);
            return Task.FromResult(Result<MailTemplate>.Ok(t));
        }

        public Task<Result<Unit>> DeleteAsync(string accountId, string templateId, CancellationToken ct)
        {
            _ = Items.RemoveAll(t => string.Equals(t.Id, templateId, StringComparison.Ordinal));
            return Task.FromResult(Result<Unit>.Ok(default));
        }
    }
}
