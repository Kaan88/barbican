// Bulwark lib/__tests__/plugin-dialog-size.test.tsx (cut limits, text-only reads, controls) and lib/plugin-sandbox/host-dialog.ts (one open dialog, 20 per minute, results).
using Barbican.Core.Common;
using Barbican.Core.Plugins.Api.Dialogs;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Plugins.Api.Dialogs;

public sealed class PluginDialogMethodsTests
{
    private static readonly PluginGrantSnapshot Grant = new(new PluginPackageIdentity("probe", "1.0.0", new string('a', 64)), 1, true, [], []);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly PluginValueLimits Big = new(MaxWireBytes: 1 << 30, MaxBinaryBytes: 1 << 20, MaxNodes: 200_000, MaxDepth: 64, MaxStringBytes: 1 << 26, MaxSequenceLength: 100_000);

    private static IPluginApiMethod Pick(IReadOnlyList<IPluginApiMethod> methods, string name) =>
        methods.Single(method => string.Equals(method.Descriptor.Name, name, StringComparison.Ordinal));

    private static PluginValue Options(Action<PluginValueBuilder, List<(string, int)>> fill)
    {
        var builder = new PluginValueBuilder();
        var properties = new List<(string, int)>();
        fill(builder, properties);
        return builder.Build(builder.Array(builder.Object([.. properties])), Big);
    }

    private static PluginValue Args(params (string Key, string Value)[] texts) =>
        Options((b, p) => p.AddRange(texts.Select(t => (t.Key, b.String(t.Value)))));

    private static Func<PluginDialogRequest, IPluginInvocationContext, CancellationToken, Task<PluginDialogOutcome>> Answer(
        PluginDialogOutcome outcome, List<PluginDialogRequest>? seen = null) =>
        (request, _, _) => { seen?.Add(request); return Task.FromResult(outcome); };

    [Fact]
    public void ThreeMethodsNeedNoPermissionAndHaveNoDeadline()
    {
        var methods = PluginDialogMethods.Create(TimeProvider.System, Answer(PluginDialogOutcome.Cancelled));
        Assert.Equal(["ui.alert", "ui.confirm", "ui.prompt"], methods.Select(m => m.Descriptor.Name), StringComparer.Ordinal);
        Assert.All(methods, method =>
        {
            Assert.Empty(method.Descriptor.PermissionAlternatives);
            Assert.Equal(Timeout.InfiniteTimeSpan, method.Descriptor.Deadline);
            Assert.Null(method.ValidateArguments(Args()));
        });
    }

    [Fact]
    public async Task AlertResolvesToUndefinedConfirmToBooleanPromptToMapOrNull()
    {
        await using var binding = PluginRuntimeBinding.Create("auto-dialog", Grant, PluginFrameKind.Background);
        var context = new Context(binding);
        var seen = new List<PluginDialogRequest>();
        var outcome = PluginDialogOutcome.Accepted(new Dictionary<string, string>(StringComparer.Ordinal) { ["user"] = "ann", ["pw"] = "" });
        var methods = PluginDialogMethods.Create(TimeProvider.System, (request, _, _) =>
        {
            seen.Add(request);
            return Task.FromResult(outcome);
        });
        var alert = await Pick(methods, "ui.alert").InvokeAsync(context, Args(("message", "hi")), Ct);
        Assert.Equal(PluginValueKind.Undefined, alert.Value!.Kind);
        Assert.True((await Pick(methods, "ui.confirm").InvokeAsync(context, Args(), Ct)).Value!.BooleanValue);
        var prompt = (await Pick(methods, "ui.prompt").InvokeAsync(context, Args(), Ct)).Value!;
        Assert.Equal("ann", prompt.Property("user").StringValue, StringComparer.Ordinal);
        Assert.Equal(string.Empty, prompt.Property("pw").StringValue, StringComparer.Ordinal);

        outcome = PluginDialogOutcome.Cancelled;
        Assert.False((await Pick(methods, "ui.confirm").InvokeAsync(context, Args(), Ct)).Value!.BooleanValue);
        Assert.Equal(PluginValueKind.Null, (await Pick(methods, "ui.prompt").InvokeAsync(context, Args(), Ct)).Value!.Kind);
        Assert.Equal(PluginValueKind.Undefined, (await Pick(methods, "ui.alert").InvokeAsync(context, Args(), Ct)).Value!.Kind);
    }

    [Fact]
    public void DefaultsAndTextOnlyReads()
    {
        var empty = PluginDialogMethods.Read(PluginDialogKind.Confirm, Args());
        Assert.Null(empty.Title);
        Assert.Equal(string.Empty, empty.Message, StringComparer.Ordinal);
        Assert.Null(empty.ConfirmLabel);
        Assert.False(empty.Danger);

        // a bare string, null, a number: all read as no options (Bulwark reads properties of the value, which a string does not have)
        var builder = new PluginValueBuilder();
        foreach (var first in new[] { builder.String("just text"), builder.Null(), builder.Number(1) })
        {
            var request = PluginDialogMethods.Read(PluginDialogKind.Alert, builder.Build(builder.Array(first)));
            Assert.Null(request.Title);
            Assert.Equal(string.Empty, request.Message, StringComparer.Ordinal);
        }

        var odd = Options((b, p) =>
        {
            p.Add(("title", b.Object(("toString", b.String("x")))));
            p.Add(("message", b.Array(b.String("x"))));
            p.Add(("confirmLabel", b.Number(3)));
            p.Add(("danger", b.String("yes")));
        });
        var read = PluginDialogMethods.Read(PluginDialogKind.Confirm, odd);
        Assert.Null(read.Title);
        Assert.Equal(string.Empty, read.Message, StringComparer.Ordinal);
        Assert.Null(read.ConfirmLabel);
        Assert.True(read.Danger);
    }

    [Fact]
    public void LongTextsAreCutNotRefused()
    {
        var read = PluginDialogMethods.Read(PluginDialogKind.Confirm, Args(("title", new string('T', 2 * 1024 * 1024)),
            ("message", "Found 3 problems:\n" + string.Concat(Enumerable.Repeat("- an IBAN in the text\n", 2000))),
            ("confirmLabel", new string('Y', 100_000)), ("cancelLabel", new string('N', 100_000))));
        Assert.Equal(PluginDialogText.MaxTitle, read.Title!.Length);
        Assert.StartsWith("Found 3 problems:\n- an IBAN in the text", read.Message, StringComparison.Ordinal);
        Assert.True(read.Message.Length <= PluginDialogText.MaxMessage);
        Assert.Equal(PluginDialogText.MaxLabel, read.ConfirmLabel!.Length);
        Assert.Equal(PluginDialogText.MaxLabel, read.CancelLabel!.Length);
        var huge = PluginDialogMethods.Read(PluginDialogKind.Alert, Args(("message", string.Concat(Enumerable.Repeat("a**", 350_000)))));
        Assert.True(huge.Message.Length <= PluginDialogText.MaxMessage);
    }

    [Fact]
    public void PromptFieldsAreFilteredCutAndLimited()
    {
        var builder = new PluginValueBuilder();
        var fields = Enumerable.Range(0, 300).Select(i => builder.Object(("name", builder.String($"f{i}")),
            ("label", builder.String(new string('L', 600))), ("placeholder", builder.String(new string('P', 600))))).ToArray();
        var arguments = builder.Build(builder.Array(builder.Object(("fields", builder.Array(fields)))), Big);
        var read = PluginDialogMethods.Read(PluginDialogKind.Prompt, arguments);
        Assert.Equal(PluginDialogText.MaxFields, read.Fields.Count);
        Assert.All(read.Fields, field =>
        {
            Assert.Equal(PluginDialogText.MaxLabel, field.Label.Length);
            Assert.Equal(PluginDialogText.MaxLabel, field.Placeholder!.Length);
        });

        var mixed = Options((b, p) => p.Add(("fields", b.Array(b.Null(), b.String("f"),
            b.Object(("name", b.Object(("a", b.Number(1)))), ("label", b.String("Nested name"))),
            b.Object(("name", b.String("ok")), ("label", b.Array(b.String("L"))), ("placeholder", b.Object(("p", b.Number(1)))),
                ("value", b.String("V")), ("options", b.Array(b.String("o")))),
            b.Object(("name", b.String("pw")), ("label", b.String("Password")), ("type", b.String("password")), ("required", b.Boolean(true))),
            b.Object(("name", b.String("x")), ("type", b.String("weird")))))));
        var kept = PluginDialogMethods.Read(PluginDialogKind.Prompt, mixed).Fields;
        Assert.Equal([new PluginDialogField("ok", string.Empty, false, null, false), new PluginDialogField("pw", "Password", true, null, true),
            new PluginDialogField("x", string.Empty, false, null, false)], kept);
        Assert.Empty(PluginDialogMethods.Read(PluginDialogKind.Confirm, mixed).Fields);
    }

    [Fact]
    public void ControlsBidiAndBrokenSurrogatesAreCleaned()
    {
        Assert.Equal("a bc", PluginDialogText.Clean("a\u0000b\u202ec", 50, multiline: false), StringComparer.Ordinal);
        Assert.Equal("one two three", PluginDialogText.Clean("one\ntwo\u2028three", 50, multiline: false), StringComparer.Ordinal);
        Assert.Equal("one\ntwo\nthree\nfour", PluginDialogText.Clean("one\r\ntwo\rthree\u0085four", 50, multiline: true), StringComparer.Ordinal);
        Assert.Equal("\ufffdx\ufffd", PluginDialogText.Clean("\ud83dx\ude00", 50, multiline: false), StringComparer.Ordinal);
        Assert.Equal(new string('x', 9), PluginDialogText.Clean(new string('x', 9) + "\ud83d\ude00", 10, multiline: false), StringComparer.Ordinal);
        Assert.Equal("Evil", PluginDialogText.Clean("\u202eEvil\u2069", 50, multiline: false), StringComparer.Ordinal);
    }

    [Fact]
    public async Task SecondDialogOfOneAddOnIsRefusedWhileOneIsOpenThenAllowed()
    {
        var release = new TaskCompletionSource<PluginDialogOutcome>();
        var calls = 0;
#pragma warning disable VSTHRD003 // the test owns the completion source it releases
        var methods = PluginDialogMethods.Create(TimeProvider.System, async (_, _, _) => { calls++; return await release.Task; });
#pragma warning restore VSTHRD003
        await using var one = PluginRuntimeBinding.Create("auto-dialog", Grant, PluginFrameKind.Background);
        await using var two = PluginRuntimeBinding.Create("auto-dialog", Grant, PluginFrameKind.Background);
        var first = Pick(methods, "ui.confirm").InvokeAsync(new Context(one), Args(), Ct);
        var refused = await Pick(methods, "ui.alert").InvokeAsync(new Context(two), Args(), Ct);
        Assert.Equal(PluginErrorCode.Conflict, refused.Error);
        Assert.Equal(1, calls);
        release.SetResult(PluginDialogOutcome.Accepted());
        Assert.True((await first).IsSuccess);
        Assert.True((await Pick(methods, "ui.alert").InvokeAsync(new Context(two), Args(), Ct)).IsSuccess);
    }

    [Fact]
    public async Task TwentyOpeningsPerMinuteThenRefusedUntilTheWindowPasses()
    {
        var clock = new FakeTimeProvider();
        var methods = PluginDialogMethods.Create(clock, Answer(PluginDialogOutcome.Accepted()));
        await using var binding = PluginRuntimeBinding.Create("auto-dialog", Grant, PluginFrameKind.Background);
        var context = new Context(binding);
        for (var i = 0; i < PluginDialogMethods.MaxPerMinute; i++)
            Assert.True((await Pick(methods, "ui.confirm").InvokeAsync(context, Args(), Ct)).IsSuccess);
        Assert.Equal(PluginErrorCode.RateLimited, (await Pick(methods, "ui.confirm").InvokeAsync(context, Args(), Ct)).Error);
        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.True((await Pick(methods, "ui.confirm").InvokeAsync(context, Args(), Ct)).IsSuccess);
    }

    [Fact]
    public async Task StaleBeforeOpenFailsWithoutDrawing()
    {
        var calls = 0;
        var methods = PluginDialogMethods.Create(TimeProvider.System, (_, _, _) => { calls++; return Task.FromResult(PluginDialogOutcome.Accepted()); });
        await using var binding = PluginRuntimeBinding.Create("auto-dialog", Grant, PluginFrameKind.Background);
        var context = new Context(binding) { Current = false };
        Assert.Equal(PluginErrorCode.StaleContext, (await Pick(methods, "ui.confirm").InvokeAsync(context, Args(), Ct)).Error);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task OpenDialogIsClosedAndFailsStaleWhenTheContextGoesStale()
    {
        var clock = new FakeTimeProvider();
        var closed = new TaskCompletionSource();
        var methods = PluginDialogMethods.Create(clock, async (_, _, token) =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { closed.SetResult(); }
            return PluginDialogOutcome.Cancelled;
        });
        await using var binding = PluginRuntimeBinding.Create("auto-dialog", Grant, PluginFrameKind.Background);
        var context = new Context(binding);
        var call = Pick(methods, "ui.prompt").InvokeAsync(context, Args(), Ct);
        context.Current = false;
        while (!closed.Task.IsCompleted)
        {
            clock.Advance(PluginDialogMethods.CurrentCheckInterval);
            await Task.Delay(5, Ct);
        }

        Assert.Equal(PluginErrorCode.StaleContext, (await call).Error);
        // the slot is free again
        context.Current = true;
        var again = PluginDialogMethods.Create(clock, Answer(PluginDialogOutcome.Accepted()));
        Assert.True((await Pick(again, "ui.alert").InvokeAsync(context, Args(), Ct)).IsSuccess);
    }

    [Fact]
    public async Task PresenterRefusalAndCallerCancellationAreFailuresAndFreeTheSlot()
    {
        var methods = PluginDialogMethods.Create(TimeProvider.System, async (_, _, token) =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { /* the host closes the dialog */ }
            return PluginDialogOutcome.Cancelled;
        });
        await using var binding = PluginRuntimeBinding.Create("auto-dialog", Grant, PluginFrameKind.Background);
        var context = new Context(binding);
        using var cancel = new CancellationTokenSource();
        var call = Pick(methods, "ui.confirm").InvokeAsync(context, Args(), cancel.Token);
        await cancel.CancelAsync();
        Assert.Equal(PluginErrorCode.Cancelled, (await call).Error);

        var refusing = PluginDialogMethods.Create(TimeProvider.System, Answer(PluginDialogOutcome.Failure(PluginErrorCode.Unavailable)));
        Assert.Equal(PluginErrorCode.Unavailable, (await Pick(refusing, "ui.alert").InvokeAsync(context, Args(), Ct)).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await Pick(refusing, "ui.alert").InvokeAsync(context, Args(), Ct)).Error);
    }

    private sealed class Context(PluginRuntimeBinding binding) : IPluginInvocationContext
    {
        public PluginRuntimeBinding Binding { get; } = binding;
        public PluginGrantSnapshot Grant => PluginDialogMethodsTests.Grant;
        public bool Current { get; set; } = true;
        bool IPluginInvocationContext.HasIssuedMutation => false;
        public ValueTask<bool> CheckCurrentAsync(CancellationToken ct) => ValueTask.FromResult(Current && Binding.IsAlive);
        public Result<PluginSessionCapture> CaptureActive() => throw new NotSupportedException();
        public Result<PluginSessionCapture> CaptureOwner(string localAccountKey) => throw new NotSupportedException();
        public Result<Unit> Demand(string permission, string? localAccountKey) => throw new NotSupportedException();
        public void MarkMutationIssued() => throw new NotSupportedException();
    }
}
