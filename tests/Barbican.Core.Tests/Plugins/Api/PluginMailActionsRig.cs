// Test wiring of the mail-action methods (email.move, archive, keywords, batch, attachment and raw reads, drafts.create, compose.open and compose.reply): the real PluginApiDispatcher over a FakeJmap login, the real writer
// and hook bus, and switchable doubles for the grant, the session, the undo history, the draft service and the composer launcher. OnWrite runs when a method asks for the writer: flip the grant or session there to go stale
// during a write.
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Mail.Write;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Api.MailActions;
using Barbican.Core.Plugins.Api.MailTags;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Bridge;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Plugins.Api;

internal sealed class PluginMailActionsRig
{
    public PluginApiRig Api { get; private set; } = null!;

    public HookBus Hooks { get; } = new(new FakeTimeProvider());

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.Zero));

    public History Undo { get; } = new();

    public Drafts DraftService { get; } = new();

    public Launcher Compose { get; } = new();

    public Action? OnWrite { get; set; }

    public FakePrefs Prefs { get; } = new();

    public static async Task<PluginMailActionsRig> StartAsync(string[] permissions)
    {
        var rig = new PluginMailActionsRig();
        rig.Api = await PluginApiRig.StartAsync(() =>
        {
            var host = new PluginMailActionHost(new Readers(), new Writers(rig), new PluginMailRateLimiter(rig.Time), new PluginTagDefinitions(rig.Prefs, rig.Prefs), rig.Time,
                rig.Hooks, rig.Undo, rig.DraftService, rig.Compose);
            return PluginMailActionMethods.Create(host);
        }, permissions);
        return rig;
    }

    public string Folder(string role) => Api.F.Server.Store.MailboxId(role)!;

    public string IdOf(string subject) =>
        Api.F.Server.Store.Emails(Api.F.Server.Store.EmailIds()).Where(e => string.Equals((string?)e["subject"], subject, StringComparison.Ordinal)).Select(e => (string)e["id"]!).First();

    public string[] FoldersOf(string id) => [.. Api.F.Server.Store.Email(id)!["mailboxIds"]!.AsObject().Select(p => p.Key)];

    public bool HasKeyword(string id, string keyword) => Api.F.Server.Store.Email(id)!["keywords"]?[keyword]?.GetValue<bool>() == true;

    public Task<PluginCallOutcome> CallAsync(string method, string args) => Api.CallAsync(method, args);

    private sealed class Readers : IPluginMailReaders
    {
        public Barbican.Core.Mail.IMailService Reader(IAccountSession session, string? sharedAccountId) => sharedAccountId is null ? session.Mail : throw new NotSupportedException();
    }

    private sealed class Writers(PluginMailActionsRig rig) : IMailWriterFactory
    {
        public IMailWriter ForSession(IAccountSession session, string? accountId = null)
        {
            rig.OnWrite?.Invoke();
            return new MailWriterFactory().ForSession(session, accountId);
        }
    }

    internal sealed class History : IPluginMailUndoHistory
    {
        public List<(string Id, string Label, Func<CancellationToken, Task<Result<Unit>>> Undo)> Entries { get; } = [];

        public string Record(string label, Func<CancellationToken, Task<Result<Unit>>> undo)
        {
            var id = "h" + (Entries.Count + 1);
            Entries.Add((id, label, undo));
            return id;
        }

        public bool Remove(string entryId) => Entries.RemoveAll(e => string.Equals(e.Id, entryId, StringComparison.Ordinal)) > 0;
    }

    internal sealed class Drafts : IDraftService
    {
        public List<ComposeDraft> Saved { get; } = [];

        public Task<Result<SavedDraft>> SaveAsync(ComposeDraft draft, CancellationToken ct)
        {
            Saved.Add(draft);
            return Task.FromResult(Result<SavedDraft>.Ok(new SavedDraft("draft-" + Saved.Count)));
        }

        public Task<Result<Unit>> DiscardAsync(string accountId, string draftId, CancellationToken ct) => Task.FromResult(Result<Unit>.Ok(default));
    }

    internal sealed class Launcher : IPluginComposeLauncher
    {
        public List<PluginComposeMessage> Opened { get; } = [];

        public List<(string Id, string Text, bool All)> Replies { get; } = [];

        public PluginComposeOpenResult Result { get; set; } = PluginComposeOpenResult.Opened;

        public Action? OnOpen { get; set; }

        public Task<PluginComposeOpenResult> OpenAsync(string accountKey, PluginComposeMessage message, CancellationToken ct)
        {
            Opened.Add(message);
            OnOpen?.Invoke();
            return Task.FromResult(Result);
        }

        public Task<PluginComposeOpenResult> ReplyAsync(string accountKey, string emailId, string text, bool replyAll, CancellationToken ct)
        {
            Replies.Add((emailId, text, replyAll));
            return Task.FromResult(Result);
        }
    }
}
