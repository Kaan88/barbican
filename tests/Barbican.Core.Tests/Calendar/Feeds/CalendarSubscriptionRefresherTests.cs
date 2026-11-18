// D-M4-I01: the stored refresh interval of a webcal subscription is run by a timer (it ran nowhere before the integration pass).

using Barbican.Core.Accounts;
using Barbican.Core.Calendar.Feeds;
using Barbican.Core.Common;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Calendar.Feeds;

public sealed class CalendarSubscriptionRefresherTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeSubscriptions _subs = new([new CalendarSubscription("cal-a", "https://feed.example/a.ics", 30), new CalendarSubscription("cal-b", "https://feed.example/b.ics", 360)]);

    [Fact]
    public async Task A_subscription_is_refreshed_once_its_interval_has_passed_and_not_before()
    {
        using var refresher = new CalendarSubscriptionRefresher(new FakeAccounts(), _subs, _clock);
        Assert.Equal(0, await refresher.RefreshDueAsync(CancellationToken.None)); // the first look starts the interval, nothing is fetched at start-up
        _clock.Advance(TimeSpan.FromMinutes(29));
        Assert.Equal(0, await refresher.RefreshDueAsync(CancellationToken.None));
        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await refresher.RefreshDueAsync(CancellationToken.None));
        Assert.Equal(["cal-a"], _subs.Refreshed.ToArray());
        _clock.Advance(TimeSpan.FromMinutes(29));
        Assert.Equal(0, await refresher.RefreshDueAsync(CancellationToken.None));
        _clock.Advance(TimeSpan.FromMinutes(331)); // 360 since the start: the six-hour feed is due, the half-hour one again
        Assert.Equal(2, await refresher.RefreshDueAsync(CancellationToken.None));
        Assert.Equal(["cal-a", "cal-a", "cal-b"], _subs.Refreshed.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task The_timer_runs_the_check_every_minute_after_Start()
    {
        using var refresher = new CalendarSubscriptionRefresher(new FakeAccounts(), _subs, _clock);
        refresher.Start();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (_subs.Refreshed.Count == 0 && DateTime.UtcNow < deadline)
        {
            _clock.Advance(CalendarSubscriptionRefresher.CheckEvery);
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.Contains("cal-a", _subs.Refreshed, StringComparer.Ordinal);
        refresher.Shutdown();
    }

    private sealed class FakeSubscriptions(IReadOnlyList<CalendarSubscription> list) : ICalendarSubscriptions
    {
        public List<string> Refreshed { get; } = [];

        public Task<IReadOnlyList<CalendarSubscription>> ListAsync(string accountKey, CancellationToken ct) => Task.FromResult(list);

        public Task<Result<string?>> SaveAsync(string accountKey, CalendarSubscription subscription, string name, CancellationToken ct) => throw new NotSupportedException();

        public Task<Result<int>> RefreshAsync(string accountKey, string calendarId, CancellationToken ct)
        {
            lock (Refreshed)
            {
                Refreshed.Add(calendarId);
            }

            return Task.FromResult(Result<int>.Ok(0));
        }

        public Task<Result<Unit>> RemoveAsync(string accountKey, string calendarId, bool deleteCalendar, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeAccounts : IAccountManager
    {
        public IReadOnlyList<AccountInfo> Accounts { get; } = [new AccountInfo("acct", "user@example.org", "example.org")];

        public AccountInfo? Active => Accounts[0];

        public event EventHandler<ActiveAccountChangedEventArgs>? ActiveChanged
        {
            add { }
            remove { }
        }

        public Task<Result<AccountInfo>> SignInWithPasswordAsync(PasswordSignIn request, CancellationToken ct) => throw new NotSupportedException();

        public Task<Result<AccountInfo>> SignInWithOAuthAsync(OAuthSignIn request, CancellationToken ct) => throw new NotSupportedException();

        public Task<Result<Unit>> SwitchToAsync(string accountKey, CancellationToken ct) => throw new NotSupportedException();

        public IAccountSession? SessionFor(string accountKey) => null;
    }
}
