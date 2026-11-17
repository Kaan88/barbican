// Waiting for background work in tests without blocking a thread (Thread.Sleep is banned): polls with short real delays. Owner jmap.

namespace Barbican.Core.Tests.Jmap;

internal static class Poll
{
    public static async Task UntilAsync(Func<bool> condition, string what, int seconds = 10)
    {
        var until = DateTimeOffset.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow > until)
            {
                Assert.Fail("timed out waiting for " + what);
            }

            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }
}
