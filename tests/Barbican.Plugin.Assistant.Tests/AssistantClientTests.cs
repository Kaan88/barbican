// Provider requests are tested against an in-memory handler; no external service is contacted.
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using static Barbican.Plugin.Assistant.Tests.AssistantTestFakes;

namespace Barbican.Plugin.Assistant.Tests;

public sealed class AssistantClientTests
{
    [Theory]
    [InlineData(AssistantProviderKind.OpenAiCompatible)]
    [InlineData(AssistantProviderKind.Anthropic)]
    public async Task Provider_request_uses_its_shape_and_parses_tool_calls(AssistantProviderKind provider)
    {
        var keys = new MemorySecrets();
        await keys.WriteAsync(AssistantClient.ApiKeyName, "fake-api-key", TestContext.Current.CancellationToken);
        var handler = new Handler(provider);
        var client = new AssistantClient(keys, new HttpClient(handler));
        var config = Config(AssistantActionMode.Ask) with { Provider = provider };
        var messages = new[] { new AssistantMessage("system", "Rules", null, null), new AssistantMessage("user", "Hello", null, null) };
        var specs = new[] { new AssistantToolSpec("search_mail", "Search", new JsonObject { ["type"] = "object" }, false) };
        var result = await client.CompleteAsync(config, messages, specs, TestContext.Current.CancellationToken);
        Assert.True(result.IsOk, result.Error);
        Assert.Equal("Checking", result.Value!.Text);
        Assert.Equal("search_mail", (string?)result.Value.ToolCalls![0]!["name"]);
        Assert.Equal("hello", (string?)result.Value.ToolCalls![0]!["arguments"]!["text"]);
        Assert.NotNull(handler.Body);
        Assert.Equal("model", (string?)handler.Body!["model"]);
        Assert.Equal(provider == AssistantProviderKind.Anthropic ? "/v1/messages" : "/v1/chat/completions", handler.Path);
        Assert.True(handler.AuthPresent);
    }

    [Fact]
    public async Task Missing_key_does_not_contact_provider()
    {
        var handler = new Handler(AssistantProviderKind.OpenAiCompatible);
        var client = new AssistantClient(new MemorySecrets(), new HttpClient(handler));
        var result = await client.CompleteAsync(Config(AssistantActionMode.Ask), [], [], TestContext.Current.CancellationToken);
        Assert.False(result.IsOk);
        Assert.Equal(AssistantErrors.NoKey, result.Error);
        Assert.Null(handler.Body);
    }

    [Theory]
    [InlineData(AssistantProviderKind.OpenAiCompatible)]
    [InlineData(AssistantProviderKind.Anthropic)]
    public async Task Blank_model_does_not_contact_provider(AssistantProviderKind provider)
    {
        var keys = new MemorySecrets();
        await keys.WriteAsync(AssistantClient.ApiKeyName, "fake-api-key", TestContext.Current.CancellationToken);
        var handler = new Handler(provider);
        var client = new AssistantClient(keys, new HttpClient(handler));
        var result = await client.CompleteAsync(Config(AssistantActionMode.Ask) with { Provider = provider, Model = "  " }, [], [], TestContext.Current.CancellationToken);
        Assert.False(result.IsOk);
        Assert.Equal("Choose a model for the AI service.", result.Error);
        Assert.Null(handler.Body);
    }

    private sealed class Handler(AssistantProviderKind provider) : HttpMessageHandler
    {
        public JsonNode? Body { get; private set; }
        public string? Path { get; private set; }
        public bool AuthPresent { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Path = request.RequestUri!.AbsolutePath;
            Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct));
            AuthPresent = provider == AssistantProviderKind.Anthropic ? request.Headers.Contains("x-api-key") : request.Headers.Authorization is not null;
            var json = provider == AssistantProviderKind.Anthropic
                ? """{"content":[{"type":"text","text":"Checking"},{"type":"tool_use","id":"c1","name":"search_mail","input":{"text":"hello"}}]}"""
                : """{"choices":[{"message":{"content":"Checking","tool_calls":[{"id":"c1","function":{"name":"search_mail","arguments":"{\"text\":\"hello\"}"}}]}}]}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
