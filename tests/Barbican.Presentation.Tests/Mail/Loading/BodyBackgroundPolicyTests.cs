// Green: BodyBackgroundPolicy (decision D-WV-01): hide + suspend on background, resume on foreground, close after N minutes, lazy re-create.
// Uses FakeTimeProvider; the host is a recording fake.

using Barbican.Presentation.Common;
using Barbican.Presentation.Mail.Loading;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Presentation.Tests.Mail.Loading;

public sealed class BodyBackgroundPolicyTests
{
    private sealed class FakeHost : IBodyWebViewHost
    {
        public List<string> Calls { get; } = [];

        public bool IsSupported { get; set; } = true;

        public bool IsCreated { get; set; } = true;

        public bool HasContent { get; set; }

        public TaskCompletionSource<bool>? Gate { get; set; }

        public void Hide() => Calls.Add("hide");

        public Task<bool> SuspendAsync()
        {
            Calls.Add("suspend");
            return Gate?.Task ?? Task.FromResult(true);
        }

        public void ResumeView() => Calls.Add("resume");

        public void Show() => Calls.Add("show");

        public void Close()
        {
            Calls.Add("close");
            IsCreated = false;
        }

        public void Recreate()
        {
            Calls.Add("recreate");
            IsCreated = true;
        }
    }

    private sealed class InlineDispatcher : IDispatcher
    {
        public bool HasThreadAccess => true;

        public void Post(Action action) => action();

        public Task InvokeAsync(Func<Task> action) => action();
    }

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeHost _host = new();
    private int _minutes = 5;

    private bool Has(string call) => _host.Calls.Contains(call, StringComparer.Ordinal);

    private BodyBackgroundPolicy Make() => new(_host, _time, new InlineDispatcher(), () => TimeSpan.FromMinutes(_minutes));

    [Fact]
    public void Background_hides_then_suspends_and_foreground_resumes_then_shows()
    {
        var p = Make();
        p.EnterBackground();
        Assert.Equal(["hide", "suspend"], _host.Calls);
        Assert.Equal(BodyPowerState.Suspended, p.State);
        p.EnterForeground();
        Assert.Equal(["hide", "suspend", "resume", "show"], _host.Calls);
        Assert.Equal(BodyPowerState.Active, p.State);
    }

    [Fact]
    public void Transitions_are_idempotent()
    {
        var p = Make();
        p.EnterForeground();
        Assert.Empty(_host.Calls);
        p.EnterBackground();
        p.EnterBackground();
        Assert.Equal(["hide", "suspend"], _host.Calls);
        p.EnterForeground();
        p.EnterForeground();
        Assert.Equal(["hide", "suspend", "resume", "show"], _host.Calls);
    }

    [Fact]
    public void Closes_after_the_configured_minutes_and_not_before()
    {
        var p = Make();
        p.EnterBackground();
        _time.Advance(TimeSpan.FromMinutes(4.9));
        Assert.Equal(BodyPowerState.Suspended, p.State);
        _time.Advance(TimeSpan.FromMinutes(0.2));
        Assert.Equal(BodyPowerState.Closed, p.State);
        Assert.True(Has("close"));
        Assert.Equal(1, p.Closes);
    }

    [Fact]
    public void Zero_minutes_never_closes()
    {
        _minutes = 0;
        var p = Make();
        p.EnterBackground();
        _time.Advance(TimeSpan.FromHours(10));
        Assert.Equal(BodyPowerState.Suspended, p.State);
        Assert.False(Has("close"));
    }

    [Fact]
    public void Foreground_before_the_deadline_cancels_the_close()
    {
        var p = Make();
        p.EnterBackground();
        _time.Advance(TimeSpan.FromMinutes(3));
        p.EnterForeground();
        _time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(BodyPowerState.Active, p.State);
        Assert.False(Has("close"));
    }

    [Fact]
    public void A_second_background_starts_a_new_wait()
    {
        var p = Make();
        p.EnterBackground();
        _time.Advance(TimeSpan.FromMinutes(4));
        p.EnterForeground();
        p.EnterBackground();
        _time.Advance(TimeSpan.FromMinutes(4));
        Assert.NotEqual(BodyPowerState.Closed, p.State);
        _time.Advance(TimeSpan.FromMinutes(1.1));
        Assert.Equal(BodyPowerState.Closed, p.State);
    }

    [Fact]
    public void Use_after_close_recreates_and_records_the_time()
    {
        var p = Make();
        p.EnterBackground();
        _time.Advance(TimeSpan.FromMinutes(6));
        _time.Advance(TimeSpan.FromMinutes(1));
        p.BeforeUse();
        Assert.Equal(BodyPowerState.Active, p.State);
        Assert.Equal(_time.GetUtcNow(), p.LastRecreated);
        Assert.Equal(1, p.Recreates);
        Assert.True(_host.IsCreated);
    }

    [Fact]
    public void Use_while_suspended_resumes_first_and_restarts_the_close_wait()
    {
        var p = Make();
        p.EnterBackground();
        _time.Advance(TimeSpan.FromMinutes(4));
        p.BeforeUse();
        Assert.Equal(BodyPowerState.Active, p.State);
        Assert.Equal("resume", _host.Calls[^1]);
        _time.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(BodyPowerState.Active, p.State);
        _time.Advance(TimeSpan.FromMinutes(1.1));
        Assert.Equal(BodyPowerState.Closed, p.State);
    }

    [Fact]
    public void Foreground_after_close_recreates_only_when_a_message_is_on_screen()
    {
        var p = Make();
        p.EnterBackground();
        _time.Advance(TimeSpan.FromMinutes(6));
        p.EnterForeground();
        Assert.Equal(BodyPowerState.Closed, p.State);
        Assert.False(Has("recreate"));

        _host.HasContent = true;
        p.EnterBackground();
        _time.Advance(TimeSpan.FromMinutes(6));
        p.EnterForeground();
        Assert.Equal(BodyPowerState.Active, p.State);
        Assert.True(Has("recreate"));
    }

    [Fact]
    public void Not_created_yet_is_safe_and_use_while_in_background_stays_hidden()
    {
        _host.IsCreated = false;
        var p = Make();
        p.EnterBackground();
        Assert.Empty(_host.Calls);
        _time.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(BodyPowerState.Active, p.State);
        p.BeforeUse();
        p.EnterForeground();
        Assert.Equal(BodyPowerState.Active, p.State);
    }

    [Fact]
    public void Foreground_while_the_suspend_runs_resumes_when_it_completes()
    {
        _host.Gate = new TaskCompletionSource<bool>();
        var p = Make();
        p.EnterBackground();
        p.EnterForeground();
        _host.Gate.SetResult(true);
        Assert.Equal(BodyPowerState.Active, p.State);
        Assert.Equal("resume", _host.Calls[^1]);
        Assert.Equal(0, p.Suspends);
    }

    [Fact]
    public void An_unsupported_platform_does_nothing()
    {
        _host.IsSupported = false;
        var p = Make();
        p.EnterBackground();
        _time.Advance(TimeSpan.FromHours(1));
        p.EnterForeground();
        Assert.Empty(_host.Calls);
        Assert.False(p.InBackground);
    }

    [Theory]
    [InlineData(null, 5)]
    [InlineData("", 5)]
    [InlineData("abc", 5)]
    [InlineData("0", 0)]
    [InlineData("-3", 0)]
    [InlineData("15", 15)]
    public void The_setting_is_parsed_with_a_default_of_five_minutes(string? value, int minutes) =>
        Assert.Equal(TimeSpan.FromMinutes(minutes), BodyBackgroundPolicy.ParseMinutes(value));
}
