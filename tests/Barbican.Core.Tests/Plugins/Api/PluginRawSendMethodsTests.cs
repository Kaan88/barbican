using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Plugins.Api.RawSend;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Core.Tests.Plugins.Api;

public sealed class PluginRawSendMethodsTests
{
    private static readonly PluginGrantSnapshot Grant = new(new PluginPackageIdentity("probe", "1.0.0", new string('a', 64)), 1, true, ["email:raw-send"], ["sample"]);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preserves_view_without_copy_and_projects_only_receipt_fields(bool submitOnly)
    {
        var host = new Host { Receipt = new("sample", "account", submitOnly ? null : "email", "submission", null) };
        var method = submitOnly ? PluginRawSendMethods.SubmitRaw(host) : PluginRawSendMethods.SendRaw(host);
        Assert.True(method.Descriptor.MayMutate);
        Assert.Contains("email:raw-send", method.Descriptor.PermissionAlternatives[0], StringComparer.Ordinal);
        var builder = new PluginValueBuilder();
        var buffer = builder.Buffer("xxRAWyy"u8);
        var args = builder.Build(builder.Array(builder.View(buffer, PluginViewType.Uint8, 2, 3), builder.String("identity"),
            builder.Object(("envelopeRecipients", builder.Array(builder.String("to@example.org"))), ("labelToken", builder.String("host-token")))));
        await using var binding = PluginRuntimeBinding.Create("auto-raw", Grant, PluginFrameKind.Background);
        var result = await method.InvokeAsync(new Context(binding), args, Ct);
        Assert.True(result.IsSuccess);
        Assert.NotNull(host.Last);
        Assert.True(host.Last.Bytes.SameNode(args.Item(0)));
        Assert.Equal("RAW"u8.ToArray(), host.Last.Bytes.BinaryBytes());
        Assert.Equal(submitOnly, host.Last.SubmitOnly);
        Assert.Equal("host-token", host.Last.LabelToken, StringComparer.Ordinal);
        Assert.Equal(["to@example.org"], host.Last.EnvelopeRecipients, StringComparer.Ordinal);
        Assert.False(result.Value!.Property("scheduled").BooleanValue);
        Assert.True(result.Value.Property("isSmime").BooleanValue);
        Assert.Equal("submission", result.Value.Property("emailSubmissionId").StringValue, StringComparer.Ordinal);
        Assert.Equal("account", result.Value.Property("submissionAccountId").StringValue, StringComparer.Ordinal);
        Assert.Equal(!submitOnly, result.Value.PropertyNames.Contains("emailId", StringComparer.Ordinal));
        Assert.DoesNotContain("localAccountKey", result.Value.PropertyNames, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Optional_recipients_and_schedule_are_passed_to_host()
    {
        var release = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var host = new Host { Receipt = new("sample", "account", "email", "submission", release) { Scheduled = true, RejectedRecipients = [new("bad@example.org", "550 refused")] } };
        var method = PluginRawSendMethods.SendRaw(host);
        var b = new PluginValueBuilder();
        var args = b.Build(b.Array(b.Buffer("raw"u8), b.String("identity"), b.Object(("delayedUntil", b.String("2030-01-02T04:04:05+01:00")))));
        await using var binding = PluginRuntimeBinding.Create("auto-raw", Grant, PluginFrameKind.Background);
        var result = await method.InvokeAsync(new Context(binding), args, Ct);
        Assert.True(result.IsSuccess);
        Assert.Null(host.Last!.EnvelopeRecipients);
        Assert.Equal(release, host.Last.ScheduledAt);
        Assert.True(result.Value!.Property("scheduled").BooleanValue);
        Assert.Equal("2030-01-02T03:04:05.000Z", result.Value.Property("sendAt").StringValue, StringComparer.Ordinal);
        Assert.Equal("bad@example.org", result.Value.Property("rejectedRecipients").Item(0).Property("email").StringValue, StringComparer.Ordinal);
        Assert.Equal("550 refused", result.Value.Property("rejectedRecipients").Item(0).Property("smtpReply").StringValue, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Invalid_values_and_host_refusals_are_safe()
    {
        var host = new Host { Error = PluginErrorCode.UnknownOutcome };
        var method = PluginRawSendMethods.SubmitRaw(host);
        var b = new PluginValueBuilder();
        var invalid = b.Build(b.Array(b.String("not bytes"), b.String("identity")));
        await using var binding = PluginRuntimeBinding.Create("auto-raw", Grant, PluginFrameKind.Background);
        var context = new Context(binding);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await method.InvokeAsync(context, invalid, Ct)).Error);
        Assert.Equal(0, host.Calls);
        b = new PluginValueBuilder();
        var valid = b.Build(b.Array(b.Buffer("raw"u8), b.String("identity")));
        Assert.Equal(PluginErrorCode.UnknownOutcome, (await method.InvokeAsync(context, valid, Ct)).Error);
        Assert.Equal(1, host.Calls);
    }

    [Fact]
    public async Task EmptyRecipientsAreUnspecifiedAndScheduleDoesNotRequireServerTime()
    {
        var host = new Host { Receipt = new("sample", "account", null, "submission", null) { Scheduled = true } };
        var method = PluginRawSendMethods.SubmitRaw(host);
        var b = new PluginValueBuilder();
        var args = b.Build(b.Array(b.Buffer("raw"u8), b.String("identity"), b.Object(("envelopeRecipients", b.Array()))));
        await using var binding = PluginRuntimeBinding.Create("auto-raw", Grant, PluginFrameKind.Background);
        var result = await method.InvokeAsync(new Context(binding), args, Ct);
        Assert.True(result.IsSuccess);
        Assert.Null(host.Last!.EnvelopeRecipients);
        Assert.True(result.Value!.Property("scheduled").BooleanValue);
        Assert.DoesNotContain("sendAt", result.Value.PropertyNames, StringComparer.Ordinal);
    }
    private sealed class Host : IPluginRawSendHost
    {
        public int Calls { get; private set; }
        public PluginRawSendRequest? Last { get; private set; }
        public RawMessageReceipt? Receipt { get; init; }
        public PluginErrorCode? Error { get; init; }
        public Task<PluginRawSendOutcome> SendAsync(IPluginInvocationContext context, PluginRawSendRequest request, CancellationToken ct)
        { Calls++; Last = request; return Task.FromResult(new PluginRawSendOutcome(Receipt, Error)); }
    }
    private sealed class Context(PluginRuntimeBinding binding) : IPluginInvocationContext
    {
        public PluginRuntimeBinding Binding { get; } = binding;
        public PluginGrantSnapshot Grant => PluginRawSendMethodsTests.Grant;
        public bool HasIssuedMutation => false;
        public ValueTask<bool> CheckCurrentAsync(CancellationToken ct) => ValueTask.FromResult(Binding.IsAlive);
        public Result<PluginSessionCapture> CaptureActive() => throw new NotSupportedException();
        public Result<PluginSessionCapture> CaptureOwner(string localAccountKey) => throw new NotSupportedException();
        public Result<Unit> Demand(string permission, string? localAccountKey) => throw new NotSupportedException();
        public void MarkMutationIssued() => throw new NotSupportedException();
    }
}
