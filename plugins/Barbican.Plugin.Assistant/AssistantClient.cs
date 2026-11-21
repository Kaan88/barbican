// Mirrors: see IAssistantClient; Rust ai.rs:84-186 (post: one request per turn, 90 s timeout, bearer key for OpenAI-compatible, x-api-key and anthropic-version 2023-06-01 for Anthropic; status mapping; chat: the two message shapes). B-AI-001..003.
// D-M6-A05: no Microsoft.Extensions.AI. Two request shapes of a few lines each are clearer than an abstraction that would hide them; the provider's answer is read only for text and tool calls. The key comes from IPluginSecrets and goes only into a header; the HttpClient is the host's.

using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Plugins;

namespace Barbican.Plugin.Assistant;

/// <summary>The default <see cref="IAssistantClient"/>.</summary>
public sealed class AssistantClient : IAssistantClient
{
    /// <summary>The longest wait for one answer (Rust ai.rs:93).</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    /// <summary>The secret name of the API key (the host maps it to the credential-store entry of this add-on).</summary>
    public const string ApiKeyName = "api-key";

    private readonly IPluginSecrets _keys;
    private readonly HttpClient _http;

    /// <summary>The client over the host's secrets and the host's HTTP client (full-trust code still sends nothing but the provider request).</summary>
    public AssistantClient(IPluginSecrets keys, HttpClient http)
    {
        _keys = keys;
        _http = http;
    }

    /// <inheritdoc/>
    public Task<AssistantResult<AssistantMessage>> CompleteAsync(AssistantConfig config, IReadOnlyList<AssistantMessage> messages, IReadOnlyList<AssistantToolSpec> tools, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(tools);
        return Task.Run(() => CompleteCoreAsync(config, messages, tools, ct), ct);
    }

    private static JsonObject OpenAiBody(AssistantConfig config, IReadOnlyList<AssistantMessage> messages, IReadOnlyList<AssistantToolSpec> tools)
    {
        var list = new JsonArray();
        foreach (var m in messages)
        {
            switch (m.Role)
            {
                case "system":
                    AssistantJson.Push(list, new JsonObject { ["role"] = "system", ["content"] = m.Text });
                    break;
                case "assistant":
                    var a = new JsonObject { ["role"] = "assistant", ["content"] = m.Text };
                    if (m.ToolCalls is JsonArray { Count: > 0 } calls)
                    {
                        a["tool_calls"] = new JsonArray([.. calls.OfType<JsonObject>().Select(c => (JsonNode?)new JsonObject
                        {
                            ["id"] = (string?)c["id"] ?? string.Empty,
                            ["type"] = "function",
                            ["function"] = new JsonObject { ["name"] = (string?)c["name"] ?? string.Empty, ["arguments"] = (c["arguments"] ?? new JsonObject()).ToJsonString() },
                        })]);
                    }

                    AssistantJson.Push(list, a);
                    break;
                case "tool":
                    AssistantJson.Push(list, new JsonObject { ["role"] = "tool", ["tool_call_id"] = m.ToolCallId ?? string.Empty, ["content"] = m.Text });
                    break;
                default:
                    AssistantJson.Push(list, new JsonObject { ["role"] = "user", ["content"] = m.Text });
                    break;
            }
        }

        var body = new JsonObject { ["model"] = config.Model, ["messages"] = list };
        if (tools.Count > 0)
        {
            body["tools"] = new JsonArray([.. tools.Select(t => (JsonNode?)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = t.Name, ["description"] = t.Description, ["parameters"] = t.Schema.DeepClone() },
            })]);
        }

        return body;
    }

    private static JsonObject AnthropicBody(AssistantConfig config, IReadOnlyList<AssistantMessage> messages, IReadOnlyList<AssistantToolSpec> tools)
    {
        var list = new JsonArray();
        var system = new StringBuilder();
        JsonArray? results = null;
        foreach (var m in messages)
        {
            if (!string.Equals(m.Role, "tool", StringComparison.Ordinal))
            {
                results = null;
            }

            switch (m.Role)
            {
                case "system":
                    _ = system.Append(m.Text);
                    break;
                case "assistant":
                    var content = new JsonArray();
                    if (m.Text.Length > 0)
                    {
                        AssistantJson.Push(content, new JsonObject { ["type"] = "text", ["text"] = m.Text });
                    }

                    foreach (var c in (m.ToolCalls as JsonArray ?? []).OfType<JsonObject>())
                    {
                        AssistantJson.Push(content, new JsonObject { ["type"] = "tool_use", ["id"] = (string?)c["id"] ?? string.Empty, ["name"] = (string?)c["name"] ?? string.Empty, ["input"] = (c["arguments"] ?? new JsonObject()).DeepClone() });
                    }

                    if (content.Count == 0)
                    {
                        AssistantJson.Push(content, new JsonObject { ["type"] = "text", ["text"] = "…" });
                    }

                    AssistantJson.Push(list, new JsonObject { ["role"] = "assistant", ["content"] = content });
                    break;
                case "tool":
                    if (results is null)
                    {
                        results = [];
                        AssistantJson.Push(list, new JsonObject { ["role"] = "user", ["content"] = results });
                    }

                    AssistantJson.Push(results, new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = m.ToolCallId ?? string.Empty, ["content"] = m.Text });
                    break;
                default:
                    AssistantJson.Push(list, new JsonObject { ["role"] = "user", ["content"] = m.Text });
                    break;
            }
        }

        var body = new JsonObject { ["model"] = config.Model, ["max_tokens"] = 4096, ["system"] = system.ToString(), ["messages"] = list };
        if (tools.Count > 0)
        {
            body["tools"] = new JsonArray([.. tools.Select(t => (JsonNode?)new JsonObject { ["name"] = t.Name, ["description"] = t.Description, ["input_schema"] = t.Schema.DeepClone() })]);
        }

        return body;
    }

    private static AssistantMessage ReadOpenAi(JsonNode? answer)
    {
        var m = answer?["choices"]?[0]?["message"];
        var calls = new JsonArray();
        foreach (var k in (m?["tool_calls"] as JsonArray ?? []).OfType<JsonObject>())
        {
            JsonNode? args = null;
            try
            {
                args = JsonNode.Parse((string?)k["function"]?["arguments"] ?? "{}");
            }
            catch (JsonException)
            {
                // a model that sent broken arguments gets an empty object, as Rust did
            }

            AssistantJson.Push(calls, new JsonObject { ["id"] = (string?)k["id"] ?? string.Empty, ["name"] = (string?)k["function"]?["name"] ?? string.Empty, ["arguments"] = args as JsonObject ?? [] });
        }

        return new AssistantMessage("assistant", ((string?)m?["content"] ?? string.Empty).Trim(), calls.Count == 0 ? null : calls, null);
    }

    private static AssistantMessage ReadAnthropic(JsonNode? answer)
    {
        var text = new StringBuilder();
        var calls = new JsonArray();
        foreach (var b in (answer?["content"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var type = (string?)b["type"];
            if (string.Equals(type, "text", StringComparison.Ordinal))
            {
                _ = text.Append((string?)b["text"]);
            }
            else if (string.Equals(type, "tool_use", StringComparison.Ordinal))
            {
                AssistantJson.Push(calls, new JsonObject { ["id"] = (string?)b["id"] ?? string.Empty, ["name"] = (string?)b["name"] ?? string.Empty, ["arguments"] = b["input"]?.DeepClone() ?? new JsonObject() });
            }
        }

        return new AssistantMessage("assistant", text.ToString().Trim(), calls.Count == 0 ? null : calls, null);
    }

#pragma warning disable MA0051 // Request setup and error mapping form one provider transaction.
    private async Task<AssistantResult<AssistantMessage>> CompleteCoreAsync(AssistantConfig config, IReadOnlyList<AssistantMessage> messages, IReadOnlyList<AssistantToolSpec> tools, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.Model))
        {
            return AssistantResult<AssistantMessage>.Fail("Choose a model for the AI service.");
        }

        var key = await _keys.ReadAsync(ApiKeyName, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(key))
        {
            return AssistantResult<AssistantMessage>.Fail(AssistantErrors.NoKey);
        }

        var anthropic = config.Provider == AssistantProviderKind.Anthropic;
        var baseUrl = config.Base.TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var root) || (!string.Equals(root.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) && !string.Equals(root.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)))
        {
            return AssistantResult<AssistantMessage>.Fail("The address of the AI service is not a web address.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + (anthropic ? "/messages" : "/chat/completions"));
        if (anthropic)
        {
            request.Headers.Add("x-api-key", key);
            request.Headers.Add("anthropic-version", "2023-06-01");
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

        var body = anthropic ? AnthropicBody(config, messages, tools) : OpenAiBody(config, messages, tools);
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            JsonNode? answer = null;
            try
            {
                answer = JsonNode.Parse(text);
            }
            catch (JsonException)
            {
                // a non-JSON answer is reported by its status
            }

            var status = (int)response.StatusCode;
            if (status != 200)
            {
                var detail = (string?)answer?["error"]?["message"];
                var retry = response.Headers.RetryAfter?.Delta ?? (response.Headers.TryGetValues("retry-after", out var values) && double.TryParse(values.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ? TimeSpan.FromSeconds(seconds) : null);
                return AssistantResult<AssistantMessage>.Fail(AssistantErrors.Message(status, retry, detail));
            }

            return AssistantResult<AssistantMessage>.Ok(anthropic ? ReadAnthropic(answer) : ReadOpenAi(answer));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return AssistantResult<AssistantMessage>.Fail("The AI service timed out.");
        }
        catch (HttpRequestException e)
        {
            return AssistantResult<AssistantMessage>.Fail("Could not reach the AI service: " + e.Message);
        }
    }
#pragma warning restore MA0051
}
