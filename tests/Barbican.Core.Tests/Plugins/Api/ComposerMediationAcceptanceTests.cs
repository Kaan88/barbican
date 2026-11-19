// Source: lib/plugin-sandbox/host-api-composer.ts:99-143; composer-registry.ts:62-86; plugin-types.ts:1470-1488.
using System.Text.Json;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Mail;
using Barbican.Core.Plugins.Api.Composer;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Core.Tests.Plugins.Api;

public sealed class ComposerMediationAcceptanceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly PluginPackageIdentity Package = new("probe", "1.0.0", new string('a', 64));
    private static PluginGrantSnapshot Grant(params string[] permissions) => new(Package, 1, true, permissions, ["a", "b"]);
    private static PluginValue Args(string json) { using var doc = JsonDocument.Parse(json); return PluginValue.FromJson(doc.RootElement); }
    private static PluginApiRequest Request(string method, string json) => new("request", method, Args(json));
    private static PluginApiDispatcher Dispatcher(IPluginApiMethod method, Authority authority, Sessions sessions) => new("auto-mediation", [method], authority, sessions);

    [Fact]
    public async Task SnapshotUsesHostExposedNonActiveOwnerAndProjectsOnlySourceFields()
    {
        var grant = Grant("compose:read");
        var authority = new Authority(grant); var sessions = new Sessions(); var access = new Access();
        var dispatcher = Dispatcher(PluginComposerMethods.GetSnapshot(access), authority, sessions);
        await using var binding = PluginRuntimeBinding.Create("auto-mediation", grant, PluginFrameKind.Background);
        var result = await dispatcher.DispatchAsync(binding, Request("composer.getSnapshot", "[\"host-handle\"]"), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal("b", Assert.Single(sessions.OwnerLookups), StringComparer.Ordinal);
        Assert.Equal(0, sessions.ActiveLookups);
        Assert.Equal("subject", result.Value!.Property("subject").StringValue, StringComparer.Ordinal);
        Assert.DoesNotContain("localAccountKey", result.Value.PropertyNames, StringComparer.Ordinal);
        Assert.DoesNotContain("identityId", result.Value.PropertyNames, StringComparer.Ordinal);
        Assert.Same(binding, access.Binding);
    }

    [Fact]
    public async Task SubjectRequiresPermissionAndNeverResolvesOrWritesWhenRefused()
    {
        var grant = Grant("compose:read"); var access = new Access();
        var dispatcher = Dispatcher(PluginComposerMethods.SetSubject(access), new Authority(grant), new Sessions());
        await using var binding = PluginRuntimeBinding.Create("auto-mediation", grant, PluginFrameKind.Background);
        var result = await dispatcher.DispatchAsync(binding, Request("composer.setSubject", "[\"host-handle\",\"hello\"]"), Ct);
        Assert.Equal(PluginErrorCode.PermissionDenied, result.Error);
        Assert.Equal(0, access.Resolutions); Assert.Equal(0, access.Handle.Writes);
    }

    [Fact]
    public async Task SubjectRejectsInvalidInputBeforeResolvingAndWritesCleanedValueToOwner()
    {
        var grant = Grant("compose:write"); var access = new Access();
        var dispatcher = Dispatcher(PluginComposerMethods.SetSubject(access), new Authority(grant), new Sessions());
        await using var binding = PluginRuntimeBinding.Create("auto-mediation", grant, PluginFrameKind.Background);
        var rejected = await dispatcher.DispatchAsync(binding, Request("composer.setSubject", "[\"host-handle\",{}]"), Ct);
        Assert.Equal(PluginErrorCode.InvalidArguments, rejected.Error); Assert.Equal(0, access.Resolutions);
        var accepted = await dispatcher.DispatchAsync(binding, Request("composer.setSubject", "[\"host-handle\",\"  a\\r\\nb  \"]"), Ct);
        Assert.True(accepted.IsSuccess); Assert.Equal("a b", access.Handle.WrittenSubject, StringComparer.Ordinal);
        Assert.Equal("rev-1", access.Handle.WrittenRevision, StringComparer.Ordinal);
    }

    [Fact]
    public async Task UndeliveredComposerIdDoesNotReachAnyBackend()
    {
        var grant = Grant("compose:read"); var access = new Access();
        var dispatcher = Dispatcher(PluginComposerMethods.GetSnapshot(access), new Authority(grant), new Sessions());
        await using var binding = PluginRuntimeBinding.Create("auto-mediation", grant, PluginFrameKind.Background);
        var outcome = await dispatcher.DispatchAsync(binding, Request("composer.getSnapshot", "[\"forged\"]"), Ct);
        Assert.False(outcome.IsSuccess); Assert.Equal(0, access.Handle.Reads);
    }

    [Fact]
    public async Task AttachmentMustBelongToSnapshotBeforeAnyRead()
    {
        var grant = Grant("email:blob-read"); var access = new Access();
        var dispatcher = Dispatcher(PluginComposerMethods.ReadAttachment(access), new Authority(grant), new Sessions());
        await using var binding = PluginRuntimeBinding.Create("auto-mediation", grant, PluginFrameKind.Background);
        var outcome = await dispatcher.DispatchAsync(binding, Request("composer.readAttachment", "[\"host-handle\",\"foreign\"]"), Ct);
        Assert.Equal(PluginErrorCode.AttachmentNotInComposer, outcome.Error); Assert.Equal(0, access.Handle.AttachmentReads);
    }

    [Fact]
    public async Task AttachmentDeclaredOverLimitIsNeverRead()
    {
        var grant = Grant("email:blob-read"); var access = new Access(); access.Handle.Size = (25 * 1024 * 1024) + 1;
        var dispatcher = Dispatcher(PluginComposerMethods.ReadAttachment(access), new Authority(grant), new Sessions());
        await using var binding = PluginRuntimeBinding.Create("auto-mediation", grant, PluginFrameKind.Background);
        var outcome = await dispatcher.DispatchAsync(binding, Request("composer.readAttachment", "[\"host-handle\",\"file\"]"), Ct);
        Assert.Equal(PluginErrorCode.InvalidArguments, outcome.Error); Assert.Equal(0, access.Handle.AttachmentReads);
    }

    [Fact]
    public async Task SnapshotIncludesRequiredRevisionAttachmentAndPendingFields()
    {
        var grant = Grant("compose:read"); var access = new Access();
        var dispatcher = Dispatcher(PluginComposerMethods.GetSnapshot(access), new Authority(grant), new Sessions());
        await using var binding = PluginRuntimeBinding.Create("auto-mediation", grant, PluginFrameKind.Background);
        var result = await dispatcher.DispatchAsync(binding, Request("composer.getSnapshot", "[\"host-handle\"]"), Ct);
        Assert.True(result.IsSuccess); Assert.Equal("rev-1", result.Value!.Property("revision").StringValue, StringComparer.Ordinal);
        Assert.Equal(1, result.Value.Property("attachments").Count);
        Assert.False(result.Value.Property("pending").BooleanValue);
    }

    [Fact]
    public async Task AttachmentReturnedBytesAboveLimitAreNeverProjected()
    {
        var grant = Grant("email:blob-read"); var access = new Access(); access.Handle.ReturnedBytes = new byte[(25 * 1024 * 1024) + 1];
        var dispatcher = Dispatcher(PluginComposerMethods.ReadAttachment(access), new Authority(grant), new Sessions());
        await using var binding = PluginRuntimeBinding.Create("auto-mediation", grant, PluginFrameKind.Background);
        var result = await dispatcher.DispatchAsync(binding, Request("composer.readAttachment", "[\"host-handle\",\"file\"]"), Ct);
        Assert.False(result.IsSuccess); Assert.Null(result.Value); Assert.Equal(1, access.Handle.AttachmentReads);
    }

    [Fact]
    public async Task ZeroByteAttachmentIsReturnedAsAnEmptyArrayBuffer()
    {
        var grant = Grant("email:blob-read"); var access = new Access(); access.Handle.Size = 0; access.Handle.ReturnedBytes = ReadOnlyMemory<byte>.Empty;
        var dispatcher = Dispatcher(PluginComposerMethods.ReadAttachment(access), new Authority(grant), new Sessions());
        await using var binding = PluginRuntimeBinding.Create("auto-mediation", grant, PluginFrameKind.Background);
        var result = await dispatcher.DispatchAsync(binding, Request("composer.readAttachment", "[\"host-handle\",\"file\"]"), Ct);
        Assert.True(result.IsSuccess); var data = result.Value!.Property("data");
        Assert.Equal(PluginValueKind.ArrayBuffer, data.Kind); Assert.Equal(0, data.ByteLength);
    }

    [Fact]
    public async Task AttachmentCopiesOnlyTheBackendMemorySlice()
    {
        var grant = Grant("email:blob-read"); var access = new Access();
        access.Handle.Size = 2; access.Handle.ReturnedBytes = new byte[] { 99, 1, 2, 88 }.AsMemory(1, 2);
        var dispatcher = Dispatcher(PluginComposerMethods.ReadAttachment(access), new Authority(grant), new Sessions());
        await using var binding = PluginRuntimeBinding.Create("auto-mediation", grant, PluginFrameKind.Background);
        var result = await dispatcher.DispatchAsync(binding, Request("composer.readAttachment", "[\"host-handle\",\"file\"]"), Ct);
        Assert.True(result.IsSuccess); Assert.Equal(new byte[] { 1, 2 }, result.Value!.Property("data").BinaryBytes());
    }
    [Fact]
    public async Task AttachmentReadDropsResultAfterRevisionAndMembershipChange()
    {
        var grant = Grant("email:blob-read"); var access = new Access(); access.Handle.Pause = true;
        var dispatcher = Dispatcher(PluginComposerMethods.ReadAttachment(access), new Authority(grant), new Sessions());
        await using var binding = PluginRuntimeBinding.Create("auto-mediation", grant, PluginFrameKind.Background);
        var pending = dispatcher.DispatchAsync(binding, Request("composer.readAttachment", "[\"host-handle\",\"file\"]"), Ct);
        await access.Handle.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        access.Handle.Revision = "rev-2"; access.Handle.Member = false; access.Handle.Release.SetResult();
        var result = await pending; Assert.Equal(PluginErrorCode.ComposerChanged, result.Error); Assert.Null(result.Value);
    }

    [Fact]
    public async Task AttachmentReadDropsResultIfHostHandleIsReplaced()
    {
        var grant = Grant("email:blob-read"); var access = new Access(); var original = access.Handle; original.Pause = true;
        var dispatcher = Dispatcher(PluginComposerMethods.ReadAttachment(access), new Authority(grant), new Sessions());
        await using var binding = PluginRuntimeBinding.Create("auto-mediation", grant, PluginFrameKind.Background);
        var pending = dispatcher.DispatchAsync(binding, Request("composer.readAttachment", "[\"host-handle\",\"file\"]"), Ct);
        await original.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        access.Handle = new Handle(); original.Release.SetResult();
        var result = await pending; Assert.Equal(PluginErrorCode.ComposerChanged, result.Error); Assert.Null(result.Value); Assert.Equal(0, access.Handle.AttachmentReads);
    }

    [Fact]
    public async Task AttachmentReadDropsResultIfOwnerSessionIsInvalidated()
    {
        var grant = Grant("email:blob-read"); var access = new Access(); access.Handle.Pause = true; var sessions = new Sessions();
        var dispatcher = Dispatcher(PluginComposerMethods.ReadAttachment(access), new Authority(grant), sessions);
        await using var binding = PluginRuntimeBinding.Create("auto-mediation", grant, PluginFrameKind.Background);
        var pending = dispatcher.DispatchAsync(binding, Request("composer.readAttachment", "[\"host-handle\",\"file\"]"), Ct);
        await access.Handle.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        sessions.Valid = false; access.Handle.Release.SetResult();
        var result = await pending; Assert.False(result.IsSuccess); Assert.Null(result.Value);
    }
    [Fact]
    public void ComposerDataCopiesSourceCollectionsAndExactByteSlice()
    {
        var recipients = new[] { "first@example.org" };
        var attachments = new[] { new PluginComposerAttachment("file", "first.txt", "text/plain", 2) };
        var snapshot = new PluginComposerSnapshot("rev", "subject", "text", recipients, [], [], attachments, false);
        var buffer = new byte[] { 9, 1, 2, 8 };
        var data = new PluginComposerAttachmentData("first.txt", "text/plain", buffer.AsMemory(1, 2));
        recipients[0] = "second@example.org"; attachments[0] = new("other", "other", "x", 0); buffer[1] = 7;
        Assert.Equal("first@example.org", snapshot.To[0], StringComparer.Ordinal);
        Assert.Equal("file", snapshot.Attachments[0].Id, StringComparer.Ordinal);
        Assert.Equal(new byte[] { 1, 2 }, data.Data.ToArray());
    }

    [Fact]
    public async Task AttachmentUsesCapturedMetadataNotBackendClaims()
    {
        var grant = Grant("email:blob-read"); var access = new Access();
        var dispatcher = Dispatcher(PluginComposerMethods.ReadAttachment(access), new Authority(grant), new Sessions());
        await using var binding = PluginRuntimeBinding.Create("auto-mediation", grant, PluginFrameKind.Background);
        var result = await dispatcher.DispatchAsync(binding, Request("composer.readAttachment", "[\"host-handle\",\"file\"]"), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal("file.txt", result.Value!.Property("name").StringValue, StringComparer.Ordinal);
        Assert.Equal("text/plain", result.Value.Property("type").StringValue, StringComparer.Ordinal);
    }
    [Fact]
    public void ComposerBusinessErrorsHaveFixedSourceCodes()
    {
        Assert.Equal("composer_changed", PluginCallOutcome.Failed(PluginErrorCode.ComposerChanged).ErrorMessage, StringComparer.Ordinal);
        Assert.Equal("attachment_not_in_composer", PluginCallOutcome.Failed(PluginErrorCode.AttachmentNotInComposer).ErrorMessage, StringComparer.Ordinal);
    }
    [Fact]
    [Red("m6b-mediation")]
    public async Task TwoFramesAndUpdatedContentCannotBypassSharedEditLimit()
    {
        var limiter = PluginComposerEditRateLimiter.Create(new Microsoft.Extensions.Time.Testing.FakeTimeProvider(), 10);
        var grant = Grant("compose:write"); var access = new Access();
        var dispatcher = Dispatcher(PluginComposerMethods.SetSubject(access, limiter), new Authority(grant), new Sessions());
        await using var first = PluginRuntimeBinding.Create("auto-mediation", grant, PluginFrameKind.Background);
        await using var second = PluginRuntimeBinding.Create("auto-mediation", grant, PluginFrameKind.Background);
        var request = Request("composer.setSubject", "[\"host-handle\",\"x\"]");
        for (var i = 0; i < 60; i++) Assert.True((await dispatcher.DispatchAsync(i % 2 == 0 ? first : second, request, Ct)).IsSuccess);
        var updatedPackage = new PluginPackageIdentity("probe", "1.0.0", new string('b', 64));
        var updated = new PluginGrantSnapshot(updatedPackage, 2, true, ["compose:write"], ["a", "b"]);
        var afterUpdate = Dispatcher(PluginComposerMethods.SetSubject(access, limiter), new Authority(updated), new Sessions());
        await using var replacement = PluginRuntimeBinding.Create("auto-mediation", updated, PluginFrameKind.Background);
        var denied = await afterUpdate.DispatchAsync(replacement, request, Ct);
        Assert.Equal(PluginErrorCode.RateLimited, denied.Error); Assert.Equal(60, access.Handle.Writes);
    }
    private sealed class Access : IPluginComposerAccess
    {
        public Handle Handle { get; set; } = new();
        public int Resolutions { get; private set; }
        public PluginRuntimeBinding? Binding { get; private set; }
        public IPluginComposerHandle? Resolve(PluginRuntimeBinding binding, string composerId)
        {
            Resolutions++; Binding = binding;
            return string.Equals(composerId, "host-handle", StringComparison.Ordinal) ? Handle : null;
        }
    }
    private sealed class Handle : IPluginComposerHandle
    {
        public string LocalAccountKey => "b";
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public int AttachmentReads { get; private set; }
        public long Size { get; set; } = 3;
        public string Revision { get; set; } = "rev-1";
        public bool Member { get; set; } = true;
        public bool Pause { get; set; }
        public ReadOnlyMemory<byte> ReturnedBytes { get; set; } = new byte[] { 1, 2, 3 };
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? WrittenSubject { get; private set; }
        public string? WrittenRevision { get; private set; }
        public Task<PluginComposerSnapshot> GetSnapshotAsync(CancellationToken ct)
        {
            Reads++;
            return Task.FromResult(new PluginComposerSnapshot(Revision, "subject", "text", ["a@example.org"], [], [], Member ? [new("file", "file.txt", "text/plain", Size)] : [], false));
        }
        public Task<bool> SetSubjectAsync(string subject, string hostRevision, CancellationToken ct) { Writes++; WrittenSubject = subject; WrittenRevision = hostRevision; return Task.FromResult(true); }
        public async Task<PluginComposerAttachmentData> ReadAttachmentAsync(string attachmentId, CancellationToken ct)
        {
            AttachmentReads++; Started.TrySetResult();
            if (Pause) await Release.Task.WaitAsync(ct);
            return new PluginComposerAttachmentData("untrusted-name", "application/untrusted", ReturnedBytes);
        }
    }
    private sealed class Authority(PluginGrantSnapshot grant) : IPluginAuthority
    {
        public event EventHandler<PluginAuthorityChangedEventArgs>? Changed { add { } remove { } }
        public Task<Result<PluginGrantSnapshot?>> GetCurrentAsync(string pluginId, CancellationToken ct) => Task.FromResult(Result<PluginGrantSnapshot?>.Ok(grant));
    }
    private sealed class Sessions : IPluginSessionSource
    {
        private readonly IAccountSession _a = new Session("a");
        private readonly IAccountSession _b = new Session("b");
        public bool Valid { get; set; } = true;
        public int ActiveLookups { get; private set; }
        public List<string> OwnerLookups { get; } = [];
        public Result<PluginSessionCapture> CaptureActive() { ActiveLookups++; return Result<PluginSessionCapture>.Ok(new(_a, 1)); }
        public Result<PluginSessionCapture> CaptureOwner(string localAccountKey) { OwnerLookups.Add(localAccountKey); return Result<PluginSessionCapture>.Ok(new(_b, null)); }
        public bool IsCurrent(PluginSessionCapture capture) => Valid && (ReferenceEquals(capture.Session, _a) || ReferenceEquals(capture.Session, _b));
    }
    private sealed class Session(string key) : IAccountSession
    {
        public AccountInfo Account { get; } = new(key, "sample@example.org", "https://jmap.example.org");
        public IJmapClient Client => throw new NotSupportedException();
        public IMailService Mail => throw new NotSupportedException();
        public IMailSync Sync => throw new NotSupportedException();
    }
}
