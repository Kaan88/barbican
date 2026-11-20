// The transport must recheck live authority at each actual HTTP attempt, including after authentication waits.
using System.Net;
using System.Net.Http.Headers;
using Barbican.Core.Auth;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Transport;

namespace Barbican.Core.Tests.Jmap;

public sealed class AuthorizedHttpAdmissionTests
{
    private static readonly Uri Target = new("https://jmap.example.org/jmap");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RevocationWhileGettingCredentialsPreventsHttpRequest()
    {
        var tokens = new PausedTokens(pauseGet: true);
        using var handler = new CountingHandler(HttpStatusCode.OK);
        using var http = new HttpClient(handler);
        var allowed = true;
        var checks = 0;
        Task<bool> BeforeSendAsync(Func<bool> issue, CancellationToken _) { checks++; return Task.FromResult(allowed && issue()); }
        var pending = SendAsync(http, tokens, BeforeSendAsync);
        await tokens.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        allowed = false;
        tokens.Release.TrySetResult();
        var result = await pending;
        Assert.False(result.IsOk);
        Assert.Equal(1, checks);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task RevocationDuringCredentialRenewalPreventsSecondHttpAttempt()
    {
        var tokens = new PausedTokens(pauseGet: false);
        using var handler = new CountingHandler(HttpStatusCode.Unauthorized);
        using var http = new HttpClient(handler);
        var allowed = true;
        var checks = 0;
        Task<bool> BeforeSendAsync(Func<bool> issue, CancellationToken _) { checks++; return Task.FromResult(allowed && issue()); }
        var pending = SendAsync(http, tokens, BeforeSendAsync);
        await tokens.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Equal(1, handler.Count);
        allowed = false;
        tokens.Release.TrySetResult();
        var result = await pending;
        Assert.False(result.IsOk);
        Assert.Equal(2, checks);
        Assert.Equal(1, handler.Count);
    }

    private static Task<Result<HttpResponseMessage>> SendAsync(HttpClient client, IAccessTokenSource tokens,
        Func<Func<bool>, CancellationToken, Task<bool>> beforeSend) =>
        AuthorizedHttp.SendAsync(client, tokens, Target, _ => true,
            () => new HttpRequestMessage(HttpMethod.Post, Target), HttpCompletionOption.ResponseContentRead, Ct, beforeSend);

    private sealed class PausedTokens(bool pauseGet) : IAccessTokenSource
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<Result<AuthenticationHeaderValue>> GetAsync(CancellationToken ct)
        {
            if (pauseGet) await PauseAsync(ct);
            return Header("first");
        }
        public async ValueTask<Result<AuthenticationHeaderValue>> RenewAfterRejectAsync(AuthenticationHeaderValue rejected, CancellationToken ct)
        {
            if (!pauseGet) await PauseAsync(ct);
            return Header("second");
        }
        private async Task PauseAsync(CancellationToken ct)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
        }
        private static Result<AuthenticationHeaderValue> Header(string token) => Result<AuthenticationHeaderValue>.Ok(new("Bearer", token));
    }

    private sealed class CountingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
