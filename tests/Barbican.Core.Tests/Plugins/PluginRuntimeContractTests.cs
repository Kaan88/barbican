using System.Text.Json;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Runtime;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Tests.Plugins;

public sealed class PluginRuntimeContractTests
{
    private static PluginPackageIdentity Package() => new("probe", "1.0.0", new string('a', 64));
    private static PluginGrantSnapshot Grant(bool enabled = true) => new(Package(), 1, enabled, ["email:read"], ["account-a"]);

    [Fact]
    public async Task BindingIdentityIsHostCreatedAndLifetimeSurvivesDisposalAsCancelled()
    {
        var a = PluginRuntimeBinding.Create("auto-plugin-test", Grant(), PluginFrameKind.Background);
        await using var b = PluginRuntimeBinding.Create("auto-plugin-test", Grant(), PluginFrameKind.Background);
        Assert.NotEqual(a.InstanceId, b.InstanceId);
        var token = a.Lifetime;
        await a.DisposeAsync();
        await a.DisposeAsync();
        Assert.False(a.IsAlive);
        Assert.True(token.IsCancellationRequested);
        Assert.True(a.Lifetime.IsCancellationRequested);
        Assert.True(b.IsAlive);
    }

    [Fact]
    public void InvalidBindingCannotStartWithDisabledAuthorityOrMismatchedFrameKind()
    {
        Assert.Throws<ArgumentException>(() => PluginRuntimeBinding.Create("auto-plugin-test", Grant(false), PluginFrameKind.Background));
        Assert.Throws<ArgumentException>(() => PluginRuntimeBinding.Create("auto-plugin-test", Grant(), PluginFrameKind.Slot));
        Assert.Throws<ArgumentException>(() => PluginRuntimeBinding.Create("auto-plugin-test", Grant(), PluginFrameKind.Background, "reader-header"));
        Assert.Throws<ArgumentException>(() => PluginRuntimeBinding.Create("../outside", Grant(), PluginFrameKind.Background));
    }

    [Fact]
    public void ApiRequestAndResultOwnTheirJsonAfterDocumentsAreDisposed()
    {
        PluginApiRequest request;
        PluginCallOutcome result;
        using (var args = JsonDocument.Parse("[{\"value\":7}]")) request = new PluginApiRequest("request-1", "storage.get", PluginValue.FromJson(args.RootElement));
        using (var value = JsonDocument.Parse("{\"value\":9}")) result = PluginCallOutcome.Succeeded(PluginValue.FromJson(value.RootElement));
        Assert.Equal(7, request.Arguments.Item(0).Property("value").ToJson().GetInt32());
        Assert.Equal(9, result.Value!.Property("value").ToJson().GetInt32());
        Assert.Null(result.Error);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(PluginValueKind.Undefined, PluginCallOutcome.Succeeded().Value!.Kind);
    }

    [Fact]
    public void JsonNullRemainsDifferentFromUndefinedAndFailureHasNoData()
    {
        using var doc = JsonDocument.Parse("null");
        Assert.Equal(PluginValueKind.Null, PluginCallOutcome.Succeeded(PluginValue.FromJson(doc.RootElement)).Value!.Kind);
        var failed = PluginCallOutcome.Failed(PluginErrorCode.PermissionDenied);
        Assert.False(failed.IsSuccess);
        Assert.Null(failed.Value);
        Assert.Equal("Plugin permission denied.", failed.ErrorMessage, StringComparer.Ordinal);
        Assert.Throws<ArgumentException>(() => new PluginApiRequest("id", "method", PluginValue.FromJson(doc.RootElement)));
    }

    [Fact]
    public void ApiBinaryArgumentsAndResultsPreserveSharedBufferIdentityAndUndefined()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var graph = new PluginValueBuilder();
        var buffer = graph.Buffer(bytes);
        var view = graph.View(buffer, PluginViewType.Uint8, 1, 2);
        var value = graph.Build(graph.Array(buffer, view, graph.Undefined()));
        var request = new PluginApiRequest("binary", "files.preview", value);
        var result = PluginCallOutcome.Succeeded(value);
        bytes[1] = 99;
        Assert.Equal(new byte[] { 2, 3 }, request.Arguments.Item(1).BinaryBytes());
        Assert.True(request.Arguments.Item(0).SameNode(result.Value!.Item(1).BackingBuffer));
        Assert.Equal(PluginValueKind.Undefined, result.Value.Item(2).Kind);
        Assert.Throws<ArgumentOutOfRangeException>(() => PluginCallOutcome.Failed((PluginErrorCode)int.MaxValue));
    }

    [Theory]
    [InlineData(PluginErrorCode.KeystoreCancelled, "keystore:cancelled")]
    [InlineData(PluginErrorCode.KeystoreAskFirst, "keystore:ask-first")]
    [InlineData(PluginErrorCode.KeystoreNotFound, "keystore:not-found")]
    [InlineData(PluginErrorCode.KeystoreUnsupported, "keystore:unsupported")]
    [InlineData(PluginErrorCode.KeystoreLocked, "keystore:locked")]
    [InlineData(PluginErrorCode.KeystoreInvalid, "keystore:invalid")]
    [InlineData(PluginErrorCode.KeystoreFailed, "keystore:failed")]
    public void KeystoreBusinessErrorsKeepSourceCodesWithoutNativeExceptionData(PluginErrorCode code, string expected)
    {
        var result = PluginCallOutcome.Failed(code);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(expected, result.ErrorMessage, StringComparer.Ordinal);
    }

    [Fact]
    public void ConsentDecisionAndPermissionAlternativesCopyMutableInputs()
    {
        var permissions = new List<string> { "email:read" };
        var accounts = new List<string> { "account-a" };
        var decision = new PluginInstallDecision("review", Package(), permissions, accounts);
        var descriptor = new PluginApiDescriptor("email.read", [permissions], TimeSpan.FromSeconds(5));
        permissions.Add("email:write");
        accounts.Add("account-b");
        Assert.DoesNotContain("email:write", decision.ApprovedPermissions);
        Assert.DoesNotContain("account-b", decision.AccountKeys);
        Assert.Single(Assert.Single(descriptor.PermissionAlternatives));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PluginApiDescriptor("email.read", [], TimeSpan.Zero));
    }

    [Fact]
    public void InertPackageMetadataOwnsManifestAndIsNotExecutionApproval()
    {
        PluginPackageDefinition definition;
        using (var manifest = JsonDocument.Parse("{\"id\":\"probe\",\"version\":\"1.0.0\"}"))
            definition = new PluginPackageDefinition(Package(), PluginRuntimeKind.JavaScript, "index.js", manifest.RootElement, [], PluginPackageSource.LocalImport);
        Assert.Equal("probe", definition.Manifest.GetProperty("id").GetString(), StringComparer.Ordinal);
        Assert.Equal(PluginExecutionTier.Untrusted, definition.RequestedTier);
        var installation = new PluginInstallation(definition, false, null);
        Assert.False(installation.Enabled);
        Assert.Null(installation.Grant);
    }
}
