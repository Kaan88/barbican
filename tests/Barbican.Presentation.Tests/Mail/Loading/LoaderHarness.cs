// Wires a MessageLoader over fakes: a scripted mail service, the real body cache, a recording body view, an inline dispatcher and a
// virtual clock. Messages m0..m9 sit in the list. Owner body.

using Barbican.Core.Mail.Cache;
using Barbican.Core.Mail.Model;
using Barbican.Core.Plugins.Hooks;
using Barbican.Presentation.Common;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Mail.Loading;
using Barbican.Presentation.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Presentation.Tests.Mail.Loading;

internal sealed class LoaderHarness : IDisposable
{
    public LoaderHarness(string prefs = "{\"disableThreading\":true}", bool canWrite = false, ReaderDepth? depth = null)
    {
        Time = new FakeTimeProvider(Mails.Now);
        Time.SetLocalTimeZone(TimeZoneInfo.Utc);
        Service = new BodyMailService();
        Active = new FakeActiveMail { Mail = Service };
        Prefs = new TestPrefs(prefs);
        Cache = new BodyCache();
        Presenter = new FakeBodyPresenter();
        Neighbours = new StaticNeighbours();
        Actions = new FakeMailActions(canWrite);
        Bus = new HookBus();
        Reader = new ReaderViewModel();
        Reader.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ReaderViewModel.Current), StringComparison.Ordinal))
            {
                Applies.Add(Reader.Current);
            }
        };
        for (var i = 0; i < 10; i++)
        {
            var id = "m" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var row = new MessageRowViewModel(Mails.Make(id, unread: i % 2 == 0, hoursAgo: i));
            Neighbours.List.Add(row);
            Service.Bodies[id] = Body(id);
        }

        Prefetcher = new MessagePrefetcher(new MessageFetcher(Cache, new NoBlobs()));
        Loader = new MessageLoader(
            Reader,
            Active,
            new MessageFetcher(Cache, new NoBlobs()),
            Prefetcher,
            Presenter,
            Neighbours,
            Actions,
            Bus,
            new InlineDispatcher(),
            Time,
            Prefs,
            () => new ReaderOptions { Now = Mails.Now, Zone = Barbican.Core.Settings.Pages.SettingDisplayTimeZone.Resolve(Prefs.Current.Str("timeZone", "auto"), Time.LocalTimeZone) },
            depth);
    }

    public FakeTimeProvider Time { get; }

    public BodyMailService Service { get; }

    public FakeActiveMail Active { get; }

    public TestPrefs Prefs { get; }

    public BodyCache Cache { get; }

    public FakeBodyPresenter Presenter { get; }

    public StaticNeighbours Neighbours { get; }

    public FakeMailActions Actions { get; }

    public HookBus Bus { get; }

    public ReaderViewModel Reader { get; }

    public MessagePrefetcher Prefetcher { get; }

    public MessageLoader Loader { get; }

    /// <summary>Every snapshot the reader took, in order.</summary>
    public List<ReaderSnapshot> Applies { get; } = [];

    public MessageRowViewModel Row(int i) => Neighbours.List[i];

    public static EmailBody Body(string id, string html = "<p>hello</p>") => new()
    {
        Id = id,
        ThreadId = "t" + id,
        Subject = "Subject " + id,
        From = [new EmailAddress("Ada", "ada@a.example.com")],
        HtmlBody = [new BodyPart { PartId = "1", Type = "text/html" }],
        TextBody = [new BodyPart { PartId = "2", Type = "text/plain" }],
        BodyValues = new Dictionary<string, BodyValue>(StringComparer.Ordinal) { ["1"] = new BodyValue(html), ["2"] = new BodyValue("hello") },
    };

    /// <summary>Puts a message in the body cache, as a prefetch would have.</summary>
    public void Cached(string id) => Cache.Put(Active.AccountKey!, Body(id), Cache.Generation);

    /// <summary>Waits until no prefetch is on its way.</summary>
    public async Task DrainPrefetchAsync()
    {
        for (var i = 0; i < 200 && Prefetcher.InFlight.Count > 0; i++)
        {
            await Task.Delay(5);
        }
    }

    public void Dispose() => Loader.Dispose();

    private sealed class NoBlobs : IBlobSource
    {
        public Task<byte[]?> DownloadAsync(Attachment part, long maxBytes, CancellationToken ct) => Task.FromResult<byte[]?>(null);
    }
}
