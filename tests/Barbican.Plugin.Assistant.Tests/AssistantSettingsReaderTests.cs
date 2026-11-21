// The assistant settings over the host's per-account add-on settings: defaults, the shape written before the assistant became an add-on, a round trip and the provider switch (B-AI-001, B-AI-006, B-SET-039).
using static Barbican.Plugin.Assistant.Tests.AssistantTestFakes;

namespace Barbican.Plugin.Assistant.Tests;

public sealed class AssistantSettingsReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Nothing_stored_reads_as_the_defaults()
    {
        var reader = new AssistantSettingsReader(new MemorySettings());
        Assert.Equal(AssistantSettingsReader.Defaults, reader.Config("a") with { Notes = [] });
        Assert.Equal(AssistantActionMode.Ask, reader.Config("a").Actions);
        Assert.True(reader.Config("a").Memory);
    }

    [Fact]
    public async Task The_pre_add_on_shape_reads_unchanged()
    {
        var store = new MemorySettings();
        await store.SetAsync("a", new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["provider"] = "1",
            ["actions"] = "2",
            ["memory"] = "false",
            ["notes"] = """["likes short answers"]""",
            ["model"] = "m",
        }, Ct);
        var config = new AssistantSettingsReader(store).Config("a");
        Assert.Equal(AssistantProviderKind.Anthropic, config.Provider);
        Assert.Equal(AssistantActionMode.Automatic, config.Actions);
        Assert.False(config.Memory);
        Assert.Equal(["likes short answers"], config.Notes);
        Assert.Equal("m", config.Model);
    }

    [Fact]
    public async Task Saving_two_accounts_keeps_each_apart()
    {
        var reader = new AssistantSettingsReader(new MemorySettings());
        await reader.SaveAsync("a", AssistantSettingsReader.Defaults with { Model = "first-model", Notes = ["n"] }, Ct);
        await reader.SaveAsync("b", AssistantSettingsReader.Defaults with { Model = "second-model" }, Ct);
        Assert.Equal("first-model", reader.Config("a").Model);
        Assert.Equal(["n"], reader.Config("a").Notes);
        Assert.Equal("second-model", reader.Config("b").Model);
    }

    [Fact]
    public async Task Switching_provider_swaps_the_standard_address_and_a_known_default_model_only()
    {
        var store = new MemorySettings();
        var reader = new AssistantSettingsReader(store);
        await store.SetAsync("a", new Dictionary<string, string?>(StringComparer.Ordinal) { ["provider"] = "1" }, Ct);
        var extra = reader.AfterChange("a", "provider");
        Assert.Equal("https://api.anthropic.com/v1", extra["base"]);
        Assert.Equal(string.Empty, extra["model"]);

        await store.SetAsync("b", new Dictionary<string, string?>(StringComparer.Ordinal) { ["provider"] = "1", ["base"] = "https://llm.example.invalid/v1", ["model"] = "mine" }, Ct);
        Assert.Empty(reader.AfterChange("b", "provider"));
        Assert.Empty(reader.AfterChange("a", "model"));
    }

    [Fact]
    public void The_plugin_declares_its_rows_and_a_chat()
    {
        var plugin = new AssistantPlugin();
        Assert.Equal("assistant", plugin.Manifest.Id);
        Assert.Contains(plugin.SettingRows, r => r.Kind == Barbican.Plugins.PluginSettingKind.Secret);
        Assert.Throws<InvalidOperationException>(() => plugin.Chat);
    }
}
