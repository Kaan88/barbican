using System.Text.Json.Nodes;
using Barbican.App.MailBody.Automation;
using Barbican.Core.Filters.Rules;
using Barbican.Platform;

namespace Barbican.Automation.Tests;

public sealed class FilterWriteRunTests
{
    [Theory]
    [InlineData("success", true, true)]
    [InlineData("foreign", false, true)]
    [InlineData("cancel-import", false, true)]
    [InlineData("cancel-save", false, false)]
    [InlineData("conflict", false, false)]
    public async Task CleanupIsVerifiedAndAmbiguousWritesRetainRecovery(string behavior, bool passed, bool complete)
    {
        var dir = Path.Combine(Path.GetTempPath(), "barbican-filter-run-" + Guid.NewGuid().ToString("N"));
        try
        {
            var backend = new FakeBackend(behavior);
            using var journal = await FilterRecoveryJournal.CreateAsync(new InMemorySecretStore(), dir, TestContext.Current.CancellationToken);
            var result = await FilterWriteRun.RunAsync(backend, journal, TestContext.Current.CancellationToken);
            Assert.Equal(passed, result.Passed);
            Assert.Equal(complete, result.CleanupComplete);
            Assert.Equal(string.Equals(behavior, "success", StringComparison.Ordinal) || string.Equals(behavior, "conflict", StringComparison.Ordinal), backend.Applied);
            Assert.Equal(!complete, File.Exists(Path.Combine(dir, "filter-recovery.lock")));
            if (string.Equals(behavior, "foreign", StringComparison.Ordinal)) Assert.True(backend.Restored && backend.MessageDestroyed && backend.FolderDestroyed);
            if (string.Equals(behavior, "cancel-save", StringComparison.Ordinal)) Assert.False(backend.Restored);
            if (string.Equals(behavior, "conflict", StringComparison.Ordinal)) Assert.False(backend.MessageDestroyed);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    private sealed class FakeBackend(string behavior) : IFilterWriteBackend
    {
        public bool Applied { get; private set; }
        public bool Restored { get; private set; }
        public bool MessageDestroyed { get; private set; }
        public bool FolderDestroyed { get; private set; }
        public Task<FilterWriteSnapshot> CaptureAsync(CancellationToken ct) => Task.FromResult(new FilterWriteSnapshot("account", "mail", "sieve", "script", "keep;", "s1"));
        public Task EnsureUniqueAsync(string token, CancellationToken ct) => Task.CompletedTask;
        public Task<string> ImportAsync(string token, CancellationToken ct) => string.Equals(behavior, "cancel-import", StringComparison.Ordinal) ? throw new OperationCanceledException() : Task.FromResult("own");
        public Task<string?> DiscoverOwnMessageAsync(string token, CancellationToken ct) => Task.FromResult<string?>("own");
        public Task<string> CreateFolderAsync(string token, CancellationToken ct) => Task.FromResult("folder");
        public Task<string?> DiscoverOwnFolderAsync(string token, CancellationToken ct) => Task.FromResult<string?>("folder");
        public Task<(string State, JsonNode Rule)> SaveRuleAsync(FilterWriteSnapshot original, string token, string folderId, CancellationToken ct)
        {
            if (string.Equals(behavior, "cancel-save", StringComparison.Ordinal)) throw new OperationCanceledException();
            return Task.FromResult<(string, JsonNode)>(("s2", new JsonObject()));
        }
        public Task<FilterScan> ScanAsync(JsonNode rule, CancellationToken ct) => Task.FromResult(new FilterScan(rule, [new FilterPlanStep("move", string.Equals(behavior, "foreign", StringComparison.Ordinal) ? ["own", "other"] : ["own"], "folder")], 1, null, 1, "e1"));
        public Task ApplyAndVerifyAsync(FilterScan scan, string emailId, string folderId, CancellationToken ct) { Applied = true; return Task.CompletedTask; }
        public Task<bool> DestroyOwnMessageAsync(string token, string emailId, CancellationToken ct) { MessageDestroyed = true; return Task.FromResult(true); }
        public Task<bool> DestroyOwnEmptyFolderAsync(string token, string folderId, CancellationToken ct) { FolderDestroyed = true; return Task.FromResult(true); }
        public Task<bool> RestoreAsync(FilterWriteSnapshot original, string expectedState, CancellationToken ct) { Restored = !string.Equals(behavior, "conflict", StringComparison.Ordinal); return Task.FromResult(Restored); }
    }
}
