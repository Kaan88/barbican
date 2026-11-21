// The services a native add-on gets in its context: its own settings, scoped secrets, the host's HTTP client, the tools and dialogs; an add-on adopts the data folder it had before add-ons existed (decision D-M6b-08).
using Barbican.Core.Plugins.Native.Host;
using Barbican.Core.Tests.Settings.Prefs;
using Barbican.Plugins;

namespace Barbican.Core.Tests.Plugins.Native.Host;

public sealed class PluginHostServicesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Apply_fills_every_service_and_scopes_the_secrets_to_the_add_on()
    {
        var root = Path.Combine(Path.GetTempPath(), "barbican-addon-services-" + Guid.NewGuid().ToString("N"));
        try
        {
            var secrets = new PluginSecretMemory();
            using var services = new PluginHostServices(new TestProfilePaths(root), secrets, () => new NoTools(), new YesDialogs());
            var context = services.Apply(new PluginContext(string.Empty, Path.Combine(root, "plugin-data", "mine")), "mine");
            Assert.NotNull(context.Settings);
            Assert.NotNull(context.Http);
            Assert.NotNull(context.Tools);
            Assert.True(await context.Dialogs!.ConfirmAsync("t", "d", Ct));
            await context.Secrets!.WriteAsync("api-key", "k", Ct);
            Assert.Equal("k", await secrets.ReadAsync("mine", "api-key", Ct));
            Assert.Null(await secrets.ReadAsync("other", "api-key", Ct));
            Assert.Same(services.SettingsOf("mine"), context.Settings);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_http_client_does_not_follow_redirects_or_keep_cookies()
    {
        var root = Path.Combine(Path.GetTempPath(), "barbican-addon-http-" + Guid.NewGuid().ToString("N"));
        using var services = new PluginHostServices(new TestProfilePaths(root), new PluginSecretMemory(), () => new NoTools());
        var context = services.Apply(new PluginContext(string.Empty, Path.Combine(root, "d")), "x");
        Assert.Equal(System.Threading.Timeout.InfiniteTimeSpan, context.Http!.Timeout);
        Assert.Null(context.Dialogs);
    }

    [Fact]
    public void An_add_on_adopts_the_data_folder_it_had_before_add_ons_without_moving_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "barbican-addon-adopt-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new TestProfilePaths(root);
            var legacy = Path.Combine(paths.DataDir, "assistant");
            Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy, "0123456789abcdef.json"), "[]");
            var target = Path.Combine(paths.DataDir, "plugin-data", "assistant");
            using var services = new PluginHostServices(paths, new PluginSecretMemory(), () => new NoTools());
            _ = services.Apply(new PluginContext(string.Empty, target), "assistant");
            Assert.True(File.Exists(Path.Combine(target, "0123456789abcdef.json")));
            Assert.True(File.Exists(Path.Combine(legacy, "0123456789abcdef.json")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class NoTools : IPluginHostTools
    {
        public IReadOnlyList<HostToolSpec> Specs(string accountKey) => [];
        public Task<string> RunAsync(string accountKey, string name, string argumentsJson, CancellationToken ct) => Task.FromResult(string.Empty);
    }

    private sealed class YesDialogs : IPluginDialogs
    {
        public Task<bool> ConfirmAsync(string title, string description, CancellationToken ct) => Task.FromResult(true);
    }
}
