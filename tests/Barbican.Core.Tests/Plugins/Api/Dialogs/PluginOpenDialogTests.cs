// Bulwark lib/plugin-sandbox/host-api.ts:2287-2310 (ui.openDialog: "slot" required, extraProps, width, fill; no method permission), host-dialog.ts (width 280-1200, one dialog per add-on, 20 a minute, results) and
// plugin-dialog-host.tsx:229-240 (onResult added last); Barbican fences: the slot's own permission, a stopped add-on, a changed grant and a stale context end or refuse the call.
using Barbican.Core.Common;
using Barbican.Core.Plugins.Api.Dialogs;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Plugins.Api.Dialogs;

public sealed class PluginOpenDialogTests
{
    private const string Held = "ui:observe";
    private static readonly PluginPackageIdentity Package = new("probe", "1.0.0", new string('a', 64));
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PluginGrantSnapshot GrantOf(params string[] permissions) => new(Package, 1, true, permissions, []);

    private static IPluginApiMethod OpenDialog(IReadOnlyList<IPluginApiMethod> methods) =>
        methods.Single(method => string.Equals(method.Descriptor.Name, "ui.openDialog", StringComparison.Ordinal));

    private static PluginValue Args(Action<PluginValueBuilder, List<(string, int)>> fill)
    {
        var builder = new PluginValueBuilder();
        var properties = new List<(string, int)>();
        fill(builder, properties);
        return builder.Build(builder.Array(builder.Object([.. properties])));
    }

    private static PluginValue Slot(string slot = "plugin-dialog") => Args((b, p) => p.Add(("slot", b.String(slot))));

    private static Func<PluginSlotDialogRequest, IPluginInvocationContext, CancellationToken, Task<PluginSlotDialogOutcome>> Present(
        PluginSlotDialogOutcome outcome, List<PluginSlotDialogRequest>? seen = null) =>
        (request, _, _) => { seen?.Add(request); return Task.FromResult(outcome); };

    private static IReadOnlyList<IPluginApiMethod> Methods(
        Func<PluginSlotDialogRequest, IPluginInvocationContext, CancellationToken, Task<PluginSlotDialogOutcome>>? present, TimeProvider? time = null) =>
        PluginDialogMethods.Create(time ?? TimeProvider.System, (_, _, _) => Task.FromResult(PluginDialogOutcome.Cancelled), present);

    [Fact]
    public void The_method_needs_no_permission_of_its_own_and_waits_for_the_user()
    {
        var method = OpenDialog(Methods(Present(PluginSlotDialogOutcome.Closed)));
        Assert.Empty(method.Descriptor.PermissionAlternatives);
        Assert.Equal(Timeout.InfiniteTimeSpan, method.Descriptor.Deadline);
    }

    [Fact]
    public void A_call_needs_a_slot_name_the_host_draws()
    {
        var method = OpenDialog(Methods(Present(PluginSlotDialogOutcome.Closed)));
        Assert.Null(method.ValidateArguments(Slot()));
        var builder = new PluginValueBuilder();
        Assert.Equal(PluginErrorCode.InvalidArguments, method.ValidateArguments(builder.Build(builder.Array())));
        Assert.Equal(PluginErrorCode.InvalidArguments, method.ValidateArguments(builder.Build(builder.Array(builder.String("plugin-dialog")))));
        Assert.Equal(PluginErrorCode.InvalidArguments, method.ValidateArguments(Args((_, _) => { })));
        Assert.Equal(PluginErrorCode.InvalidArguments, method.ValidateArguments(Args((b, p) => p.Add(("slot", b.Number(3))))));
        Assert.Equal(PluginErrorCode.InvalidArguments, method.ValidateArguments(Slot("")));
        // a slot the add-on may own but the dialog host does not draw
        Assert.Equal(PluginErrorCode.InvalidArguments, method.ValidateArguments(Slot("message-row-actions")));
        Assert.Equal(PluginErrorCode.InvalidArguments, method.ValidateArguments(Slot("not-a-slot")));
    }

    [Fact]
    public async Task Without_the_slots_permission_the_call_is_denied_and_nothing_is_drawn()
    {
        var seen = new List<PluginSlotDialogRequest>();
        var method = OpenDialog(Methods(Present(PluginSlotDialogOutcome.Closed, seen)));
        await using var binding = PluginRuntimeBinding.Create("auto-dialog", GrantOf("ui:toolbar", "ui:app-page"), PluginFrameKind.Background);
        var denied = await method.InvokeAsync(new Context(binding, ["ui:toolbar", "ui:app-page"]), Slot(), Ct);
        Assert.Equal(PluginErrorCode.PermissionDenied, denied.Error);
        Assert.Empty(seen);

        // the denial did not take the add-on's one dialog slot
        await using var holder = PluginRuntimeBinding.Create("auto-dialog", GrantOf(Held), PluginFrameKind.Background);
        Assert.True((await method.InvokeAsync(new Context(holder, [Held]), Slot(), Ct)).IsSuccess);
        Assert.Single(seen);
    }

    [Fact]
    public async Task The_slot_is_drawn_with_its_catalogue_permission_and_Bulwarks_defaults_and_limits()
    {
        var seen = new List<PluginSlotDialogRequest>();
        var method = OpenDialog(Methods(Present(PluginSlotDialogOutcome.Closed, seen)));
        await using var binding = PluginRuntimeBinding.Create("auto-dialog", GrantOf(Held), PluginFrameKind.Background);
        var context = new Context(binding, [Held]);

        Assert.True((await method.InvokeAsync(context, Slot(), Ct)).IsSuccess);
        var plain = Assert.Single(seen);
        Assert.Equal("plugin-dialog", plain.Slot);
        Assert.Equal(Held, plain.Permission);
        Assert.Null(plain.Title);
        Assert.Null(plain.Width);
        Assert.False(plain.Fill);
        Assert.Equal(PluginValueKind.Object, plain.ExtraProps.Kind);
        Assert.Equal(0, plain.ExtraProps.Count);

        var odd = Args((b, p) =>
        {
            p.Add(("slot", b.String("plugin-dialog")));
            p.Add(("title", b.String(new string('T', 5000))));
            p.Add(("width", b.Number(50)));
            p.Add(("fill", b.String("yes")));
            p.Add(("extraProps", b.Array(b.Number(1))));
            p.Add(("anchor", b.String("trigger")));
        });
        Assert.True((await method.InvokeAsync(context, odd, Ct)).IsSuccess);
        var read = seen[1];
        Assert.Equal(PluginDialogText.MaxTitle, read.Title!.Length);
        Assert.Equal(280, read.Width);
        Assert.False(read.Fill);
        Assert.Equal(0, read.ExtraProps.Count);

        var wide = Args((b, p) =>
        {
            p.Add(("slot", b.String("plugin-dialog")));
            p.Add(("width", b.Number(5000)));
            p.Add(("fill", b.Boolean(true)));
            p.Add(("extraProps", b.Object(("folder", b.String("/docs")))));
        });
        Assert.True((await method.InvokeAsync(context, wide, Ct)).IsSuccess);
        var last = seen[2];
        Assert.Equal(1200, last.Width);
        Assert.True(last.Fill);
        Assert.Equal("/docs", last.ExtraProps.Property("folder").StringValue, StringComparer.Ordinal);
    }

    [Fact]
    public async Task The_call_resolves_to_what_the_slot_answered_or_null_when_the_user_closed_the_dialog()
    {
        var builder = new PluginValueBuilder();
        var answer = builder.Build(builder.Object(("picked", builder.String("/docs/a.txt")), ("n", builder.Number(2))));
        await using var binding = PluginRuntimeBinding.Create("auto-dialog", GrantOf(Held), PluginFrameKind.Background);
        var context = new Context(binding, [Held]);

        var answered = await OpenDialog(Methods(Present(PluginSlotDialogOutcome.Answered(answer)))).InvokeAsync(context, Slot(), Ct);
        Assert.True(answered.IsSuccess);
        Assert.Equal("/docs/a.txt", answered.Value!.Property("picked").StringValue, StringComparer.Ordinal);

        var closed = await OpenDialog(Methods(Present(PluginSlotDialogOutcome.Closed))).InvokeAsync(context, Slot(), Ct);
        Assert.True(closed.IsSuccess);
        Assert.Equal(PluginValueKind.Null, closed.Value!.Kind);

        var failed = await OpenDialog(Methods(Present(PluginSlotDialogOutcome.Failure(PluginErrorCode.Unavailable)))).InvokeAsync(context, Slot(), Ct);
        Assert.Equal(PluginErrorCode.Unavailable, failed.Error);
    }

    [Fact]
    public async Task Without_a_presenter_the_call_is_unavailable()
    {
        await using var binding = PluginRuntimeBinding.Create("auto-dialog", GrantOf(Held), PluginFrameKind.Background);
        Assert.Equal(PluginErrorCode.Unavailable, (await OpenDialog(Methods(null)).InvokeAsync(new Context(binding, [Held]), Slot(), Ct)).Error);
    }

    [Fact]
    public async Task A_stopped_add_on_or_a_changed_grant_refuses_the_call_before_anything_is_drawn()
    {
        var seen = new List<PluginSlotDialogRequest>();
        var method = OpenDialog(Methods(Present(PluginSlotDialogOutcome.Closed, seen)));

        var stopped = PluginRuntimeBinding.Create("auto-dialog", GrantOf(Held), PluginFrameKind.Background);
        await stopped.DisposeAsync();
        Assert.Equal(PluginErrorCode.StaleContext, (await method.InvokeAsync(new Context(stopped, [Held]), Slot(), Ct)).Error);

        // the grant changed after the call began (generation, permissions or accounts): the live check fails
        await using var changed = PluginRuntimeBinding.Create("auto-dialog", GrantOf(Held), PluginFrameKind.Background);
        Assert.Equal(PluginErrorCode.StaleContext, (await method.InvokeAsync(new Context(changed, [Held]) { Current = false }, Slot(), Ct)).Error);
        Assert.Empty(seen);
    }

    [Fact]
    public async Task The_dialog_is_closed_and_the_call_fails_when_the_context_goes_stale_while_it_is_open()
    {
        var clock = new FakeTimeProvider();
        var closed = new TaskCompletionSource();
        var method = OpenDialog(Methods(async (_, _, token) =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { closed.SetResult(); }
            return PluginSlotDialogOutcome.Closed;
        }, clock));
        await using var binding = PluginRuntimeBinding.Create("auto-dialog", GrantOf(Held), PluginFrameKind.Background);
        var context = new Context(binding, [Held]);
        var call = method.InvokeAsync(context, Slot(), Ct);
        context.Current = false;
        while (!closed.Task.IsCompleted)
        {
            clock.Advance(PluginDialogMethods.CurrentCheckInterval);
            await Task.Delay(5, Ct);
        }

        Assert.Equal(PluginErrorCode.StaleContext, (await call).Error);
    }

    [Fact]
    public async Task A_result_that_arrives_after_the_context_went_stale_is_dropped()
    {
        var builder = new PluginValueBuilder();
        var answer = builder.Build(builder.String("late"));
        await using var binding = PluginRuntimeBinding.Create("auto-dialog", GrantOf(Held), PluginFrameKind.Background);
        var context = new Context(binding, [Held]);
        var method = OpenDialog(Methods((_, _, _) =>
        {
            context.Current = false;
            return Task.FromResult(PluginSlotDialogOutcome.Answered(answer));
        }));
        var outcome = await method.InvokeAsync(context, Slot(), Ct);
        Assert.Equal(PluginErrorCode.StaleContext, outcome.Error);
        Assert.Null(outcome.Value);
    }

    [Fact]
    public async Task One_dialog_per_add_on_is_shared_with_alert_confirm_and_prompt()
    {
        var release = new TaskCompletionSource<PluginSlotDialogOutcome>();
        var calls = 0;
#pragma warning disable VSTHRD003 // the test owns the completion source it releases
        var methods = Methods(async (_, _, _) => { calls++; return await release.Task; });
#pragma warning restore VSTHRD003
        await using var one = PluginRuntimeBinding.Create("auto-dialog", GrantOf(Held), PluginFrameKind.Background);
        await using var two = PluginRuntimeBinding.Create("auto-dialog", GrantOf(Held), PluginFrameKind.Background);
        var first = OpenDialog(methods).InvokeAsync(new Context(one, [Held]), Slot(), Ct);
        var second = await OpenDialog(methods).InvokeAsync(new Context(two, [Held]), Slot(), Ct);
        Assert.Equal(PluginErrorCode.Conflict, second.Error);
        var confirm = methods.Single(method => string.Equals(method.Descriptor.Name, "ui.confirm", StringComparison.Ordinal));
        var builder = new PluginValueBuilder();
        Assert.Equal(PluginErrorCode.Conflict, (await confirm.InvokeAsync(new Context(two, [Held]), builder.Build(builder.Array()), Ct)).Error);
        Assert.Equal(1, calls);
        release.SetResult(PluginSlotDialogOutcome.Closed);
        Assert.True((await first).IsSuccess);
        Assert.True((await OpenDialog(methods).InvokeAsync(new Context(two, [Held]), Slot(), Ct)).IsSuccess);
    }

    [Fact]
    public async Task Twenty_openings_a_minute_then_refused_until_the_window_passes()
    {
        var clock = new FakeTimeProvider();
        var method = OpenDialog(Methods(Present(PluginSlotDialogOutcome.Closed), clock));
        await using var binding = PluginRuntimeBinding.Create("auto-dialog", GrantOf(Held), PluginFrameKind.Background);
        var context = new Context(binding, [Held]);
        for (var i = 0; i < PluginDialogMethods.MaxPerMinute; i++) Assert.True((await method.InvokeAsync(context, Slot(), Ct)).IsSuccess);
        Assert.Equal(PluginErrorCode.RateLimited, (await method.InvokeAsync(context, Slot(), Ct)).Error);
        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.True((await method.InvokeAsync(context, Slot(), Ct)).IsSuccess);
    }

    [Fact]
    public async Task Caller_cancellation_fails_the_call_and_frees_the_slot()
    {
        var methods = Methods(async (_, _, token) =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { /* the host closes the dialog */ }
            return PluginSlotDialogOutcome.Closed;
        });
        await using var binding = PluginRuntimeBinding.Create("auto-dialog", GrantOf(Held), PluginFrameKind.Background);
        var context = new Context(binding, [Held]);
        using var cancel = new CancellationTokenSource();
        var call = OpenDialog(methods).InvokeAsync(context, Slot(), cancel.Token);
        await cancel.CancelAsync();
        Assert.Equal(PluginErrorCode.Cancelled, (await call).Error);
        Assert.Equal(PluginErrorCode.Cancelled, (await OpenDialog(methods).InvokeAsync(context, Slot(), new CancellationToken(true))).Error);
    }

    [Fact]
    public void The_slot_gets_the_add_ons_props_and_an_onResult_marker_the_add_on_cannot_replace()
    {
        var builder = new PluginValueBuilder();
        var extra = builder.Build(builder.Object(("folder", builder.String("/docs")), ("onResult", builder.String("mine")),
            ("nested", builder.Object(("deep", builder.Number(1))))));
        var props = PluginSlotDialogProps.Build(extra, "abc123");
        Assert.Equal(["folder", "nested", "onResult"], props.PropertyNames.Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.Equal("/docs", props.Property("folder").StringValue, StringComparer.Ordinal);
        Assert.Equal("1", props.Property("nested").Property("deep").NumberText);
        Assert.Equal("abc123", props.Property("onResult").Property(PluginHostCallbacks.MarkerKey).StringValue, StringComparer.Ordinal);
    }

    private sealed class Context(PluginRuntimeBinding binding, string[] permissions) : IPluginInvocationContext
    {
        public PluginRuntimeBinding Binding { get; } = binding;
        public PluginGrantSnapshot Grant => GrantOf(permissions);
        public bool Current { get; set; } = true;
        bool IPluginInvocationContext.HasIssuedMutation => false;
        public ValueTask<bool> CheckCurrentAsync(CancellationToken ct) => ValueTask.FromResult(Current && Binding.IsAlive);
        public Result<PluginSessionCapture> CaptureActive() => throw new NotSupportedException();
        public Result<PluginSessionCapture> CaptureOwner(string localAccountKey) => throw new NotSupportedException();
        public Result<Unit> Demand(string permission, string? localAccountKey) =>
            permissions.Contains(permission) ? Result<Unit>.Ok(default) : Result<Unit>.Fail(new Barbican.Core.Jmap.Errors.ValidationError("denied"));
        public void MarkMutationIssued() => throw new NotSupportedException();
    }
}
