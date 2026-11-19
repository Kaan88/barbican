using Barbican.App.MailBody.Automation;
using Barbican.Core.Accounts;
using Barbican.Core.Jmap;
using Barbican.Core.Filters.Rules;
using System.Text.Json.Nodes;
using Barbican.Core.Mail;
using Barbican.Core.Mail.Service;
using Barbican.Core.Mail.Reader;
using Barbican.Core.Mail.Write;
using Barbican.Core.Plugins.Hooks;
using Barbican.FakeJmap;
using Barbican.Platform;

namespace Barbican.Automation.Tests;

public sealed class FilterVerificationBackendTests
{
    [Fact]
    public async Task RealFilterServiceRoundTripRestoresOriginalScriptAndSyntheticArtifacts()
    {
        using var server = new FakeJmapServer(new FakeJmapOptions { SieveCapability = true });
        const string original = "keep;\r\n";
        server.Api.SeedSieve("filters", original, true);
        using var client = await FakeJmapHarness.ConnectAsync(server, TimeProvider.System, ct: TestContext.Current.CancellationToken);
        var session = new TestSession(client);
        var backend = new FilterVerificationBackend(() => session, session, new HookBus(TimeProvider.System), new MailWriterFactory(), TimeProvider.System);
        var before = server.Store.EmailIds().Order(StringComparer.Ordinal).ToArray();
        var dir = Path.Combine(Path.GetTempPath(), "barbican-filter-e2e-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var journal = await FilterRecoveryJournal.CreateAsync(new InMemorySecretStore(), dir, TestContext.Current.CancellationToken);
            var result = await FilterWriteRun.RunAsync(backend, journal, TestContext.Current.CancellationToken);
            Assert.True(result.Passed, $"{result}; {string.Join(", ", server.Requests)}");
            Assert.True(result.CleanupComplete);
            Assert.Equal(original, server.Api.ActiveSieveText(), StringComparer.Ordinal);
            Assert.Equal(before, server.Store.EmailIds().Order(StringComparer.Ordinal), StringComparer.Ordinal);
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task FakeServerFailureAndCancellationRestoreOrRetainConflict(bool cancellation, bool conflict)
    {
        using var server = new FakeJmapServer(new FakeJmapOptions { SieveCapability = true });
        const string original = "keep;\r\n";
        server.Api.SeedSieve("filters", original, true);
        using var client = await FakeJmapHarness.ConnectAsync(server, TimeProvider.System, ct: TestContext.Current.CancellationToken);
        var session = new TestSession(client);
        var actual = new FilterVerificationBackend(() => session, session, new HookBus(TimeProvider.System), new MailWriterFactory(), TimeProvider.System);
        var backend = new FaultBackend(actual, () =>
        {
            if (conflict) server.Api.SeedSieve("other", "# concurrent", true);
            if (cancellation) throw new OperationCanceledException();
            throw new InvalidOperationException("synthetic failure");
        });
        var before = server.Store.EmailIds().Order(StringComparer.Ordinal).ToArray();
        var dir = Path.Combine(Path.GetTempPath(), "barbican-filter-fault-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var journal = await FilterRecoveryJournal.CreateAsync(new InMemorySecretStore(), dir, TestContext.Current.CancellationToken);
            var result = await FilterWriteRun.RunAsync(backend, journal, TestContext.Current.CancellationToken);
            Assert.False(result.Passed);
            Assert.Equal(!conflict, result.CleanupComplete);
            if (conflict) Assert.Equal("# concurrent", server.Api.SieveText("other"), StringComparer.Ordinal);
            else Assert.Equal(original, server.Api.ActiveSieveText(), StringComparer.Ordinal);
            Assert.Equal(conflict, File.Exists(Path.Combine(dir, "filter-recovery.lock")));
            if (!conflict) Assert.Equal(before, server.Store.EmailIds().Order(StringComparer.Ordinal), StringComparer.Ordinal);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task MultipleResultsBlockTheRun()
    {
        using var server = new FakeJmapServer(new FakeJmapOptions { SieveCapability = true });
        const string original = "keep;\r\n";
        server.Api.SeedSieve("filters", original, true);
        var existing = server.Store.Deliver(new FakeMail { Subject = "other synthetic message", FromEmail = "rx@example.org" });
        var before = server.Store.Email(existing)!.ToJsonString();
        using var client = await FakeJmapHarness.ConnectAsync(server, TimeProvider.System, ct: TestContext.Current.CancellationToken);
        var session = new TestSession(client);
        var backend = new FilterVerificationBackend(() => session, session, new HookBus(TimeProvider.System), new MailWriterFactory(), TimeProvider.System);
        var dir = Path.Combine(Path.GetTempPath(), "barbican-filter-candidate-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var journal = await FilterRecoveryJournal.CreateAsync(new InMemorySecretStore(), dir, TestContext.Current.CancellationToken);
            var result = await FilterWriteRun.RunAsync(backend, journal, TestContext.Current.CancellationToken);
            Assert.False(result.Passed);
            Assert.True(result.CleanupComplete);
            Assert.Equal(before, server.Store.Email(existing)!.ToJsonString(), StringComparer.Ordinal);
            Assert.Equal(original, server.Api.ActiveSieveText(), StringComparer.Ordinal);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("keep; discard;")]
    [InlineData("/* @metadata:begin {\"version\":1,\"rules\":[]} @metadata:end */ keep;")]
    public async Task NonInertOriginalScriptIsRefusedBeforeAnyWrite(string script)
    {
        using var server = new FakeJmapServer(new FakeJmapOptions { SieveCapability = true });
        server.Api.SeedSieve("filters", script, true);
        using var client = await FakeJmapHarness.ConnectAsync(server, TimeProvider.System, ct: TestContext.Current.CancellationToken);
        var session = new TestSession(client);
        var backend = new FilterVerificationBackend(() => session, session, new HookBus(TimeProvider.System), new MailWriterFactory(), TimeProvider.System);
        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.CaptureAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain(server.Requests, r => r.Contains("/set", StringComparison.Ordinal) || r.Contains("upload", StringComparison.Ordinal));
        Assert.Equal(script, server.Api.ActiveSieveText(), StringComparer.Ordinal);
    }

    [Fact]
    public async Task StaleMailboxStateCannotDeleteAnotherChangedFolder()
    {
        using var server = new FakeJmapServer();
        using var client = await FakeJmapHarness.ConnectAsync(server, TimeProvider.System, ct: TestContext.Current.CancellationToken);
        var folder = server.Store.AddMailbox("synthetic folder", null);
        var stale = server.Store.State("Mailbox");
        server.Store.AddMailbox("concurrent child", null, folder);
        var result = await ReaderCalls.CallAsync(client, "Mailbox/set", new JsonObject
        {
            ["accountId"] = client.Session.MailAccountId, ["ifInState"] = stale, ["destroy"] = new JsonArray(folder),
        }, TestContext.Current.CancellationToken);
        Assert.False(result.IsOk);
        Assert.True(server.Store.HasMailbox(folder));
    }

    [Fact]
    public async Task RevokedCapturedClientStopsCleanupAndRetainsJournal()
    {
        using var server = new FakeJmapServer(new FakeJmapOptions { SieveCapability = true });
        server.Api.SeedSieve("filters", "keep;\r\n", true);
        using var client = await FakeJmapHarness.ConnectAsync(server, TimeProvider.System, ct: TestContext.Current.CancellationToken);
        var session = new TestSession(client);
        IAccountSession? authorized = session;
        var actual = new FilterVerificationBackend(() => authorized, session, new HookBus(TimeProvider.System), new MailWriterFactory(), TimeProvider.System);
        var backend = new FaultBackend(actual, () => { authorized = null; throw new OperationCanceledException(); });
        var dir = Path.Combine(Path.GetTempPath(), "barbican-filter-revoked-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var journal = await FilterRecoveryJournal.CreateAsync(new InMemorySecretStore(), dir, TestContext.Current.CancellationToken);
            var result = await FilterWriteRun.RunAsync(backend, journal, TestContext.Current.CancellationToken);
            Assert.False(result.Passed);
            Assert.False(result.CleanupComplete);
            Assert.True(File.Exists(Path.Combine(dir, "filter-recovery.lock")));
            Assert.Contains("Barbican filter test", server.Api.ActiveSieveText(), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    private sealed class FaultBackend(IFilterWriteBackend inner, Action fault) : IFilterWriteBackend
    {
        public Task<FilterWriteSnapshot> CaptureAsync(CancellationToken ct) => inner.CaptureAsync(ct);
        public Task EnsureUniqueAsync(string token, CancellationToken ct) => inner.EnsureUniqueAsync(token, ct);
        public Task<string> ImportAsync(string token, CancellationToken ct) => inner.ImportAsync(token, ct);
        public Task<string?> DiscoverOwnMessageAsync(string token, CancellationToken ct) => inner.DiscoverOwnMessageAsync(token, ct);
        public Task<string> CreateFolderAsync(string token, CancellationToken ct) => inner.CreateFolderAsync(token, ct);
        public Task<string?> DiscoverOwnFolderAsync(string token, CancellationToken ct) => inner.DiscoverOwnFolderAsync(token, ct);
        public Task<(string State, JsonNode Rule)> SaveRuleAsync(FilterWriteSnapshot original, string token, string folderId, CancellationToken ct) => inner.SaveRuleAsync(original, token, folderId, ct);
        public Task<FilterScan> ScanAsync(JsonNode rule, CancellationToken ct) { fault(); return inner.ScanAsync(rule, ct); }
        public Task ApplyAndVerifyAsync(FilterScan scan, string emailId, string folderId, CancellationToken ct) => inner.ApplyAndVerifyAsync(scan, emailId, folderId, ct);
        public Task<bool> DestroyOwnMessageAsync(string token, string emailId, CancellationToken ct) => inner.DestroyOwnMessageAsync(token, emailId, ct);
        public Task<bool> DestroyOwnEmptyFolderAsync(string token, string folderId, CancellationToken ct) => inner.DestroyOwnEmptyFolderAsync(token, folderId, ct);
        public Task<bool> RestoreAsync(FilterWriteSnapshot original, string expectedState, CancellationToken ct) => inner.RestoreAsync(original, expectedState, ct);
    }

    private sealed class TestSession(IJmapClient client) : IAccountSession
    {
        public AccountInfo Account { get; } = new("test", "test@example.org", "example.org");
        public IJmapClient Client => client;
        public IMailService Mail { get; } = new MailService(client, TimeProvider.System);
        public IMailSync Sync => throw new NotSupportedException();
    }
}
