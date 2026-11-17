// The actions under test, wired to the fake JMAP server: a real MailService and MailWriter, the message list, the sidebar's folder tree, the toast and undo
// services, and a scripted confirmer. Owner actions.

using System.Collections.Specialized;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Service;
using Barbican.Core.Mail.Write;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Settings.Accessibility;
using Barbican.FakeJmap;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Common;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Shell;
using Barbican.Presentation.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Presentation.Tests.Actions;

internal sealed class ActionHarness : IDisposable
{
    private ActionHarness(FakeJmapServer server, Core.Jmap.Transport.JmapClient client, bool readOnly, string prefs)
    {
        Server = server;
        Time = new FakeTimeProvider(Mails.Now);
        Time.SetLocalTimeZone(TimeZoneInfo.Utc);
        var service = new MailService(client, Time);
        Active = new FakeActiveMail { Mail = service, Sync = new FakeMailSync(), AccountKey = "acct" };
        Prefs = new TestPrefs(prefs);
        Opener = new RecordingOpener();
        Bus = new HookBus();
        var dispatcher = new InlineDispatcher();
        Hub = new MailEventHub(Active, dispatcher);
        var system = new NoSystemAccessibility();
        Motion = new MotionState(Time, dispatcher);
        List = new MessageListViewModel(Active, Hub, Bus, Opener, Prefs, system, Motion, Time);
        Sidebar = new SidebarViewModel(List, Hub, Prefs, system, Bus);
        Writers = new FixedWriters(new MailWriter(client, server.Options.AccountId), !readOnly);
        Toasts = new ToastService(Prefs, Time);
        Undo = new UndoService(Toasts);
        Confirmer = new ScriptedConfirmer();
        Actions = new MailActionService(Writers, Sidebar, List, Bus, Undo, Toasts, Prefs, Confirmer, Active, Time);
        List.Rows.CollectionChanged += (_, e) => Changes.Add(e.Action);
    }

    public FakeJmapServer Server { get; }

    public FakeTimeProvider Time { get; }

    public FakeActiveMail Active { get; }

    public TestPrefs Prefs { get; }

    public RecordingOpener Opener { get; }

    public HookBus Bus { get; }

    public MailEventHub Hub { get; }

    public MotionState Motion { get; }

    public MessageListViewModel List { get; }

    public SidebarViewModel Sidebar { get; }

    public FixedWriters Writers { get; }

    public ToastService Toasts { get; }

    public UndoService Undo { get; }

    public ScriptedConfirmer Confirmer { get; }

    public MailActionService Actions { get; }

    public List<NotifyCollectionChangedAction> Changes { get; } = [];

    public int Requests => Server.ApiCalls.Count;

    public static async Task<ActionHarness> StartAsync(FakeJmapOptions? options = null, bool readOnly = false, string prefs = "{}")
    {
        var server = new FakeJmapServer(options);
        var client = await FakeJmapHarness.ConnectAsync(server, TimeProvider.System, null, readOnly, TestContext.Current.CancellationToken);
        var h = new ActionHarness(server, client, readOnly, prefs);
        var tree = await h.Active.Mail!.GetMailboxesAsync(TestContext.Current.CancellationToken);
        h.Sidebar.Update(tree.Value!);
        return h;
    }

    public string Folder(string role) => Server.Store.MailboxId(role)!;

    /// <summary>Opens a folder in the list and forgets the notifications of the first fill.</summary>
    public async Task OpenAsync(string role)
    {
        var box = Sidebar.Tree.All.First(m => string.Equals(Core.Jmap.Json.MailboxRoleConverter.Name(m.Role), role, StringComparison.Ordinal));
        await List.OpenFolderAsync(box.Id, box.Role, box.Name);
        Changes.Clear();
    }

    public IReadOnlyList<MailTarget> Pick(params int[] indexes)
    {
        var rows = List.Messages.ToList();
        return [.. indexes.Select(i => new MailTarget("acct", string.Empty, rows[i].Id, rows[i].Summary.ThreadId ?? string.Empty, rows[i].Id, rows[i].Summary.MailboxIds, rows[i].Summary.Keywords))];
    }

    public void Dispose()
    {
        List.Dispose();
        Sidebar.Dispose();
        Hub.Dispose();
        Motion.Dispose();
        Toasts.Dispose();
    }

    internal sealed class FixedWriters(IMailWriter writer, bool canWrite) : IMailWriterSource
    {
        public IMailWriter? WriterOf(string accountKey) => writer;

        public bool CanWrite(string accountKey) => canWrite;
    }

    internal sealed class ScriptedConfirmer : IActionConfirmer
    {
        public bool Answer { get; set; } = true;

        public List<ConfirmRequest> Asked { get; } = [];

        public Task<bool> ConfirmAsync(ConfirmRequest request, CancellationToken ct)
        {
            Asked.Add(request);
            return Task.FromResult(Answer);
        }
    }
}
