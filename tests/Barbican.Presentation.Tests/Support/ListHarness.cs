// Wires the list, sidebar, shell and key router over fakes (no plugin host, inline dispatcher, a controllable clock). Owner mailcore.

using System.Collections.Specialized;
using Barbican.Core.Compose;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Settings.Accessibility;
using Barbican.Presentation.Common;
using Barbican.Presentation.Input;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Shell;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Presentation.Tests.Support;

internal sealed class ListHarness : IDisposable
{
    public ListHarness(string prefs = "{}", IDispatcher? dispatcher = null, IScheduledSends? scheduled = null)
    {
        Time = new FakeTimeProvider(Mails.Now);
        Time.SetLocalTimeZone(TimeZoneInfo.Utc);
        Service = new FakeMailService();
        Sync = new FakeMailSync();
        Active = new FakeActiveMail { Mail = Service, Sync = Sync };
        Prefs = new TestPrefs(prefs);
        Opener = new RecordingOpener();
        Bus = new HookBus();
        dispatcher ??= new InlineDispatcher();
        Hub = new MailEventHub(Active, dispatcher);
        System = new NoSystemAccessibility();
        Motion = new MotionState(Time, dispatcher);
        List = new MessageListViewModel(Active, Hub, Bus, Opener, Prefs, System, Motion, Time, dispatcher: dispatcher, scheduled: scheduled is null ? null : [scheduled]);
        Shell = new ShellViewModel(Prefs, System, Motion, Bus, Time, dispatcher);
        Sidebar = new SidebarViewModel(List, Hub, Prefs, System, Bus, dispatcher);
        Keys = new KeyRouter(List, Sidebar, Shell, Prefs, System, Bus, Time);
        List.Rows.CollectionChanged += (_, e) => Changes.Add(e.Action);
    }

    public FakeTimeProvider Time { get; }

    public FakeMailService Service { get; }

    public FakeMailSync Sync { get; }

    public FakeActiveMail Active { get; }

    public TestPrefs Prefs { get; }

    public RecordingOpener Opener { get; }

    public HookBus Bus { get; }

    public MailEventHub Hub { get; }

    public NoSystemAccessibility System { get; }

    public MotionState Motion { get; }

    public MessageListViewModel List { get; }

    public ShellViewModel Shell { get; }

    public SidebarViewModel Sidebar { get; }

    public KeyRouter Keys { get; }

    /// <summary>Every notification the list's Rows collection sent.</summary>
    public List<NotifyCollectionChangedAction> Changes { get; } = [];

    public void Dispose()
    {
        List.Dispose();
        Sidebar.Dispose();
        Shell.Dispose();
        Hub.Dispose();
        Motion.Dispose();
    }
}
