#pragma warning disable MA0048 // test-only account and session fakes live with the composition test
using Barbican.Core.Accounts;
using Barbican.Core.Calendar;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Mail;
using Barbican.Core.Storage;
using Barbican.Presentation.Tests.Support;
using Barbican.FakeJmap;
using Barbican.Presentation.Calendar.Editors;
using Barbican.Presentation.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.Presentation.Tests.Calendar.Editors;

public sealed class CalendarEditorCompositionTests
{
    [Fact]
    public async Task Production_factory_connects_real_participant_service_to_new_event()
    {
        var calendars = new FakeCalendars();
        calendars.AddParticipantIdentity("one", "Organizer Name", "mailto:organizer@example.org", true);
        using var server = new FakeJmapServer(new FakeJmapOptions { CalendarsCapability = true, Calendars = calendars });
        var ct = TestContext.Current.CancellationToken;
        var client = await FakeJmapHarness.ConnectAsync(server, TimeProvider.System, _ => { }, false, ct);
        var source = new FakeSource();
        var writer = new FakeWriter();
        var services = new ServiceCollection();
        services.AddSingleton<IAccountManager>(new TestAccounts(client));
        services.AddSingleton<IPrefsStore>(new TestPrefs("{\"timeZone\":\"Asia/Tokyo\"}"));
        services.AddSingleton<IEditorSource>(source);
        services.AddSingleton<ICalendarWriter>(writer);
        services.AddBarbicanCalendarData();
        services.AddBarbicanCalendarUiViewModels();
        using var provider = services.BuildServiceProvider();
        var editors = provider.GetRequiredService<CalendarEditors>();
        await editors.OpenNewEventAsync(new NewEventRequest(new DateTime(2026, 11, 20, 10, 0, 0), new DateTime(2026, 11, 20, 11, 0, 0), false), ct);
        var editor = Assert.IsType<EventEditorViewModel>(editors.Host.Card);
        Assert.Equal("Asia/Tokyo", editor.TimeZone);
        editor.GuestInput = "guest@example.org";
        Assert.True(editor.CommitGuest());
        Assert.True(await editor.SaveAsync(ct));
        var data = Assert.Single(writer.Saves).Data;
        Assert.Equal("Asia/Tokyo", (string?)data["timeZone"]);
        Assert.Equal("mailto:organizer@example.org", (string?)data["organizerCalendarAddress"]);
        var organizer = data["participants"]!.AsObject().Select(p => p.Value).Single(p => (bool?)p?["roles"]?["owner"] == true);
        Assert.Equal("Organizer Name", (string?)organizer!["name"]);
    }

    private sealed class TestAccounts(IJmapClient client) : IAccountManager
    {
        public IReadOnlyList<AccountInfo> Accounts => [];
        public AccountInfo? Active => null;
        public event EventHandler<ActiveAccountChangedEventArgs>? ActiveChanged { add { } remove { } }
        public Task<Result<AccountInfo>> SignInWithPasswordAsync(PasswordSignIn request, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<AccountInfo>> SignInWithOAuthAsync(OAuthSignIn request, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<Unit>> SwitchToAsync(string accountKey, CancellationToken ct) => throw new NotSupportedException();
        public IAccountSession? SessionFor(string accountKey) => string.Equals(accountKey, "acct", StringComparison.Ordinal) ? new TestSession(client) : null;
    }

    private sealed class TestSession(IJmapClient client) : IAccountSession
    {
        public AccountInfo Account => new("acct", "me@example.org", "example.org");
        public IJmapClient Client => client;
        public IMailService Mail => throw new NotSupportedException();
        public IMailSync Sync => throw new NotSupportedException();
    }
}
