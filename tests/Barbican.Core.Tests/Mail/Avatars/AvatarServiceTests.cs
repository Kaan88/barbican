// M2 plan §6.3 step 9 exit test "avatars against fake HTTP/DNS": off by default (no request at all); the initials stay (null) until the picture is
// ready, then Arrived fires; every fetch goes through IGuardedHttp; a BIMI record is read through the TXT resolver; onAvatarResolve sees the request;
// the disk cache answers the next run without the network.

using Barbican.Core.Common;
using Barbican.Core.Mail.Avatars;
using Barbican.Core.Net;
using Barbican.Core.Mail.Reader.Hooks;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Sync;

namespace Barbican.Core.Tests.Mail.Avatars;

public sealed class AvatarServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "barbican-avatars-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Off_by_default_nothing_is_fetched()
    {
        var http = new FakeHttp();
        var service = new AvatarService(http, () => default, NullHookBus.Instance, TimeProvider.System, null, null);
        Assert.Null(service.TryGet("anna@acme.example", null, junk: false));
        Assert.Empty(http.Urls);
    }

    [Fact]
    public async Task The_initials_stay_until_the_favicon_arrives_through_guarded_http()
    {
        // onAvatarResolve: a plugin names another https URL, and that one is fetched
        var http = new FakeHttp { ["https://cdn.acme.example/a.png"] = TestPng.Solid(4, 4, 200, 10, 10, 255) };
        var hooks = new PluginUrl("https://cdn.acme.example/a.png");
        var service = new AvatarService(http, () => new AvatarPrefs(Favicons: true, Gravatar: false, InJunk: false), hooks, TimeProvider.System, null, _dir);
        var arrived = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Arrived += (_, e) => arrived.TrySetResult(e.Email);

        Assert.Null(service.TryGet("anna@mail.acme.example", null, junk: false));
        Assert.Equal("anna@mail.acme.example", await arrived.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        var image = service.TryGet("anna@mail.acme.example", null, junk: false);
        Assert.NotNull(image);
        Assert.Equal(64, image.Picture.Width); // ToRaster fills the 64 px avatar tile
        Assert.Equal(["https://cdn.acme.example/a.png"], http.Urls);
        Assert.Equal(["https://acme.example/favicon.ico"], hooks.Asked);

        // the next run reads the disk cache: no request
        var offline = new FakeHttp();
        var again = new AvatarService(offline, () => new AvatarPrefs(true, false, false), NullHookBus.Instance, TimeProvider.System, null, _dir);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        again.Arrived += (_, _) => second.TrySetResult();
        Assert.Null(again.TryGet("anna@mail.acme.example", null, junk: false));
        await second.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Empty(offline.Urls);
    }

    [Fact]
    public async Task A_bimi_record_is_read_through_the_resolver_and_an_svg_falls_back_to_the_favicon()
    {
        var http = new FakeHttp
        {
            ["https://brand.example/logo.svg"] = "<svg/>"u8.ToArray(),
            ["https://brand.example/favicon.ico"] = TestPng.Solid(2, 2, 1, 2, 3, 255),
        };
        var asked = new List<string>();
        var service = new AvatarService(
            http,
            () => new AvatarPrefs(true, false, false),
            NullHookBus.Instance,
            TimeProvider.System,
            (name, _) =>
            {
                asked.Add(name);
                return Task.FromResult<string?>("v=BIMI1; l=https://brand.example/logo.svg");
            },
            null);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Arrived += (_, _) => done.TrySetResult();

        // the BIMI slot goes missing (SVG), the favicon is asked next
        Assert.Null(service.TryGet("news@brand.example", "brand.example", junk: false));
        for (var i = 0; i < 200 && service.TryGet("news@brand.example", "brand.example", junk: false) is null; i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.Equal(["default._bimi.brand.example"], asked);
        Assert.Contains("https://brand.example/logo.svg", http.Urls, StringComparer.Ordinal);
        Assert.Contains("https://brand.example/favicon.ico", http.Urls, StringComparer.Ordinal);
    }

    [Fact]
    public void Junk_gets_no_picture_unless_allowed()
    {
        var http = new FakeHttp();
        var service = new AvatarService(http, () => new AvatarPrefs(true, true, InJunk: false), NullHookBus.Instance, TimeProvider.System, null, null);
        Assert.Null(service.TryGet("spam@bad.example.net", null, junk: true));
        Assert.Empty(http.Urls);
    }

    private sealed class PluginUrl(string url) : IHookBus
    {
        public List<string?> Asked { get; } = [];

        public bool HasHandlers(string hook) => string.Equals(hook, HookNames.OnAvatarResolve, StringComparison.Ordinal);

        public ValueTask NotifyAsync<TPayload>(string hook, TPayload payload, CancellationToken ct) => ValueTask.CompletedTask;

        public ValueTask<bool> InterceptAsync<TPayload>(string hook, TPayload payload, CancellationToken ct) => ValueTask.FromResult(true);

        public ValueTask<T> TransformAsync<T>(string hook, T value, CancellationToken ct)
        {
            if (value is AvatarRequest r)
            {
                Asked.Add(r.Url);
                return ValueTask.FromResult((T)(object)(r with { Url = url }));
            }

            return ValueTask.FromResult(value);
        }
    }

    private sealed class FakeHttp : IGuardedHttp
    {
        private readonly Dictionary<string, byte[]> _answers = new(StringComparer.Ordinal);
        private readonly List<string> _urls = [];

        public IReadOnlyList<string> Urls
        {
            get
            {
                lock (_urls)
                {
                    return [.. _urls];
                }
            }
        }

        public byte[] this[string url]
        {
            set => _answers[url] = value;
        }

        public Task<Result<GuardedResponse>> SendAsync(GuardedRequest request, CancellationToken ct)
        {
            lock (_urls)
            {
                _urls.Add(request.Uri.AbsoluteUri);
            }

            return Task.FromResult(_answers.TryGetValue(request.Uri.AbsoluteUri, out var body)
                ? Result<GuardedResponse>.Ok(new GuardedResponse(200, body, "image/png"))
                : Result<GuardedResponse>.Ok(new GuardedResponse(404, ReadOnlyMemory<byte>.Empty, null)));
        }
    }
}
