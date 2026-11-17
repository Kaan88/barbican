// onBeforeExternalLink: asked before a link leaves the app; false stops it; only http(s) opens (mailto waits for the composer, M3).

using Barbican.Core.Common;
using Barbican.Core.Mail.Body;
using Barbican.Core.Mail.Reader.Hooks;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Sync;

namespace Barbican.Core.Tests.Mail.Body;

public sealed class CheckedLinkRouterTests
{
    [Fact]
    public async Task A_plugin_can_stop_a_link_and_mailto_is_not_opened()
    {
        var opened = new List<Uri>();
        Task<Result<Unit>> OpenAsync(Uri u, CancellationToken _)
        {
            opened.Add(u);
            return Task.FromResult(Result<Unit>.Ok(default));
        }

        var ct = TestContext.Current.CancellationToken;
        var router = new CheckedLinkRouter(NullHookBus.Instance, OpenAsync);
        Assert.True(await router.OpenAsync(new Uri("https://example.org/a"), "mail", ct));
        Assert.False(await router.OpenAsync(new Uri("mailto:a@example.org"), "mail", ct));

        var stopping = new CheckedLinkRouter(new StopLinks(), OpenAsync);
        Assert.False(await stopping.OpenAsync(new Uri("https://example.org/b"), "unsubscribe", ct));
        Assert.Equal(1, stopping.Stopped);
        Assert.Equal([new Uri("https://example.org/a")], opened);
    }

    private sealed class StopLinks : IHookBus
    {
        public bool HasHandlers(string hook) => string.Equals(hook, HookNames.OnBeforeExternalLink, StringComparison.Ordinal);

        public ValueTask NotifyAsync<TPayload>(string hook, TPayload payload, CancellationToken ct) => ValueTask.CompletedTask;

        public ValueTask<bool> InterceptAsync<TPayload>(string hook, TPayload payload, CancellationToken ct) =>
            ValueTask.FromResult(payload is not ExternalLinkContext { Source: "unsubscribe" });

        public ValueTask<T> TransformAsync<T>(string hook, T value, CancellationToken ct) => ValueTask.FromResult(value);
    }
}
