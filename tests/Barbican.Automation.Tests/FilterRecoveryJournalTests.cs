using Barbican.App.MailBody.Automation;
using Barbican.Platform;

namespace Barbican.Automation.Tests;

public sealed class FilterRecoveryJournalTests
{
    [Fact]
    public async Task JournalIsEncryptedAndBlocksAnotherRunUntilVerifiedCleanup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "barbican-filter-test-" + Guid.NewGuid().ToString("N"));
        var secrets = new InMemorySecretStore();
        try
        {
            using var journal = await FilterRecoveryJournal.CreateAsync(secrets, dir, TestContext.Current.CancellationToken);
            await journal.SaveAsync("synthetic private script", TestContext.Current.CancellationToken);
            var file = Assert.Single(Directory.GetFiles(dir, "*.json"));
            Assert.DoesNotContain("synthetic private script", await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken), StringComparison.Ordinal);
            Assert.Single(secrets.Names);
            using var reopened = await FilterRecoveryJournal.OpenAsync(secrets, dir, TestContext.Current.CancellationToken);
            Assert.Equal("synthetic private script", await reopened.ReadAsync(TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<IOException>(() => FilterRecoveryJournal.CreateAsync(secrets, dir, TestContext.Current.CancellationToken));
            await journal.CompleteAsync(TestContext.Current.CancellationToken);
            Assert.Empty(secrets.Names);
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task OversizedSnapshotKeepsRecoveryMarkerAndKey()
    {
        var dir = Path.Combine(Path.GetTempPath(), "barbican-filter-test-" + Guid.NewGuid().ToString("N"));
        var secrets = new InMemorySecretStore();
        try
        {
            using var journal = await FilterRecoveryJournal.CreateAsync(secrets, dir, TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<InvalidOperationException>(() => journal.SaveAsync(new string('x', 128 * 1024 + 1), TestContext.Current.CancellationToken));
            Assert.True(File.Exists(Path.Combine(dir, "filter-recovery.lock")));
            Assert.Single(secrets.Names);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
