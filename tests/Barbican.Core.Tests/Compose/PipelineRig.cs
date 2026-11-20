// Test wiring of the send pipeline over the fake JMAP server (M3 stream send-pipeline): the real services on a connected client, a virtual clock shared with the server's held submissions, a recording hook bus and a text codec that stands in for
// editor-core's DocHtml (which may not have landed in this branch: the pipeline reaches it through IDocCodec).

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Editor;
using Barbican.Core.Jmap;
using Barbican.Core.Mail;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Storage;
using Barbican.Core.Tests.Jmap;
using Barbican.FakeJmap;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Compose;

internal sealed class PipelineRig
{
    public const string Account = "acct";

    private PipelineRig(Fake fake, FakeTimeProvider clock, HookBus hooks, IPrefsStore prefs, IDocCodec codec, IOutgoingAddOnHooks? addOns, Func<IJmapClient, IJmapClient>? wrapClient)
    {
        Fake = fake;
        Clock = clock;
        Hooks = hooks;
        Prefs = prefs;
        var accounts = new FakeAccounts(wrapClient?.Invoke(fake.Client) ?? fake.Client, clock);
        Accounts = accounts;
        Codec = codec;
        Identities = new IdentityService(accounts);
        Seeder = new ComposeSeeder(accounts, Identities, prefs, hooks, Codec, clock);
        Drafts = new DraftService(accounts, Identities, hooks, Codec);
        Sender = new MailSender(accounts, Identities, Seeder, hooks, Codec, clock, addOns);
        Scheduled = new ScheduledSends(accounts, clock);
        Templates = new TemplateStore(accounts, clock);
        Uploader = new AttachmentUploader(accounts, hooks);
    }

    public IAccountManager Accounts { get; }

    public Fake Fake { get; }

    public FakeTimeProvider Clock { get; }

    public HookBus Hooks { get; }

    public IPrefsStore Prefs { get; }

    public IDocCodec Codec { get; }

    public IdentityService Identities { get; }

    public ComposeSeeder Seeder { get; }

    public DraftService Drafts { get; }

    public MailSender Sender { get; }

    public ScheduledSends Scheduled { get; }

    public TemplateStore Templates { get; }

    public AttachmentUploader Uploader { get; }

    public FakeStore Store => Fake.Server.Store;

    public FakeApi Api => Fake.Server.Api;

    public static FakeJmapOptions M3Options(bool futureRelease = true, bool stalwart = true, long maxDelayedSend = 30L * 86400) => new()
    {
        SeedM3 = true,
        Stalwart = stalwart,
        MaxDelayedSend = maxDelayedSend,
        SubmissionExtensions = futureRelease ? ["FUTURERELEASE"] : [],
        MetadataCapability = true,
        Identities = FakeSeed.IdentitiesM3,
        StrictMailFrom = true,
    };

    public static async Task<PipelineRig> StartAsync(FakeJmapOptions? options = null, JsonObject? prefs = null, IDocCodec? codec = null, IOutgoingAddOnHooks? addOns = null, Func<IJmapClient, IJmapClient>? wrapClient = null)
    {
        var clock = Fake.VirtualClock();
        var o = (options ?? M3Options()) with { Clock = clock };
        var fake = await Fake.StartAsync(o, clock);
        return new PipelineRig(fake, clock, new HookBus(clock), new FixedPrefs(PrefsSnapshot.FromJson(prefs ?? [])), codec ?? new TestCodec(), addOns, wrapClient);
    }

    /// <summary>The id of the seeded message with <paramref name="subject"/>.</summary>
    public string Seeded(string subject) => Store.EmailIds().First(id => string.Equals((string?)Store.Email(id)?["subject"], subject, StringComparison.Ordinal));

    /// <summary>The ids of the messages in a folder, by role.</summary>
    public IReadOnlyList<string> InFolder(string role)
    {
        var box = Store.MailboxId(role)!;
        return [.. Store.EmailIds().Where(id => Store.Email(id)!["mailboxIds"] is JsonObject m && m.ContainsKey(box))];
    }

    public static ComposeDraft NewDraft(string to = "erin@example.net", string subject = "Hello", string body = "Hi there") => new()
    {
        AccountId = Account,
        IdentityId = "i1",
        To = to,
        Subject = subject,
        Body = new Doc([Block.Para(body)]),
    };

    private sealed class FixedPrefs(PrefsSnapshot current) : IPrefsStore
    {
        public PrefsSnapshot Current => current;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }
    }

    private sealed class FakeAccounts(IJmapClient client, TimeProvider time) : IAccountManager
    {
        public IReadOnlyList<AccountInfo> Accounts => [];

        public AccountInfo? Active => null;

        public event EventHandler<ActiveAccountChangedEventArgs>? ActiveChanged
        {
            add { }
            remove { }
        }

        public Task<Result<AccountInfo>> SignInWithPasswordAsync(PasswordSignIn request, CancellationToken ct) => throw new NotSupportedException();

        public Task<Result<AccountInfo>> SignInWithOAuthAsync(OAuthSignIn request, CancellationToken ct) => throw new NotSupportedException();

        public Task<Result<Unit>> SwitchToAsync(string accountKey, CancellationToken ct) => throw new NotSupportedException();

        private readonly IAccountSession _session = new Session(client, time);
        public IAccountSession? SessionFor(string accountKey) => string.Equals(accountKey, Account, StringComparison.Ordinal) ? _session : null;
    }

    private sealed class Session(IJmapClient client, TimeProvider time) : IAccountSession
    {
        public AccountInfo Account => new(PipelineRig.Account, "user", "example.org");

        public IJmapClient Client => client;

        public IMailService Mail { get; } = new Barbican.Core.Mail.Service.MailService(client, time);

        public IMailSync Sync => throw new NotSupportedException();
    }
}
