// Mirrors: Rust agent.rs:493-531 (at most 16 model turns, "Stopped after too many steps."; cancel checked each turn, "Stopped."; intermediate text reported as progress; B-AI-007), :confirmation rules (B-AI-009: Ask mode asks before a write tool, send_email always asks, a declined action returns "The user declined this action. Do not retry it unless they ask.").
// A static method over its collaborators so the loop is testable with a scripted client. Contract from the M6 plan section 4.4; owner set-data.

using System.Text.Json.Nodes;

namespace Barbican.Plugin.Assistant;

/// <summary>The agent loop.</summary>
public static class AssistantLoop
{
    /// <summary>The most model turns one question may take.</summary>
    public const int MaxTurns = 16;

    /// <summary>The longest tool result handed back to the model.</summary>
    public const int MaxToolResult = 40_000;

    /// <summary>What the model reads after a refusal (Rust agent.rs).</summary>
    public const string Declined = "The user declined this action. Do not retry it unless they ask.";

    /// <summary>Runs the conversation to an answer: calls the model, runs the tools it asks for (asking <paramref name="confirm"/> first when the mode says so), appends everything to <paramref name="history"/>, and returns the final text. <paramref name="progress"/> gets the text of turns that also ask for tools.</summary>
    /// <remarks><paramref name="confirm"/> gets the tool name and a sentence saying what it would do.</remarks>
    public static async Task<AssistantResult<string>> RunAsync(
        IAssistantClient client,
        IAssistantTools tools,
        AssistantConfig config,
        string accountKey,
        IList<AssistantMessage> history,
        Func<string, string, Task<bool>> confirm,
        Action<string> progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(confirm);
        ArgumentNullException.ThrowIfNull(progress);
        var specs = tools.Specs(accountKey, config.Actions);
        for (var turn = 0; turn < MaxTurns; turn++)
        {
            if (ct.IsCancellationRequested)
            {
                return AssistantResult<string>.Ok("Stopped.");
            }

            var answer = await client.CompleteAsync(config, [.. history], specs, ct).ConfigureAwait(false);
            if (!answer.IsOk)
            {
                return AssistantResult<string>.Fail(answer.Error!);
            }

            var message = answer.Value!;
            history.Add(message);
            if (message.ToolCalls is not JsonArray { Count: > 0 } calls)
            {
                return AssistantResult<string>.Ok(message.Text);
            }

            if (message.Text.Trim().Length > 0)
            {
                progress(message.Text.Trim());
            }

            foreach (var call in calls.OfType<JsonObject>())
            {
                var id = (string?)call["id"] ?? string.Empty;
                var result = await RunCallAsync(tools, specs, config, accountKey, call, confirm, progress, ct).ConfigureAwait(false);
                history.Add(new AssistantMessage("tool", result.Length > MaxToolResult ? result[..MaxToolResult] : result, null, id));
            }
        }

        return AssistantResult<string>.Ok("Stopped after too many steps.");
    }

    private static async Task<string> RunCallAsync(IAssistantTools tools, IReadOnlyList<AssistantToolSpec> specs, AssistantConfig config, string accountKey, JsonObject call, Func<string, string, Task<bool>> confirm, Action<string> progress, CancellationToken ct)
    {
        var name = (string?)call["name"] ?? string.Empty;
        var args = call["arguments"] as JsonObject ?? [];
        var spec = specs.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));
        if (spec is null)
        {
            return "Error: unknown tool " + name;
        }

        progress(Label(name, args));
        var needsAsking = spec.Writes && (config.Actions == AssistantActionMode.Ask || IsDestructive(name));
        if (needsAsking && !await confirm(name, ConfirmationText(name, args)).ConfigureAwait(false))
        {
            return Declined;
        }

        try
        {
            return await tools.RunAsync(accountKey, name, args, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // a tool that throws is a text the model can read (B-AI-007), never a crash of the chat
        catch (Exception e)
#pragma warning restore CA1031
        {
            return "Error: " + e.Message;
        }
    }

    private static string ConfirmationText(string name, JsonObject args) => string.Equals(name, "send_email", StringComparison.Ordinal)
        ? $"Send email\nTo: {args["to"]}\nCc: {args["cc"]}\nBcc: {args["bcc"]}\nSubject: {args["subject"]}\n\n{args["body"]}"
        : Label(name, args);

    private static bool IsDestructive(string name) => name is "send_email" or "delete_messages" or "remove_filter_rule";

    /// <summary>"Search mail: invoice": the tool name in words with its first useful argument (Rust agent.rs label).</summary>
    public static string Label(string name, JsonObject args)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(args);
        var human = name.Replace('_', ' ');
        var hint = string.Empty;
        foreach (var key in new[] { "text", "from", "folder", "query", "title", "subject", "to", "mark", "tag", "name" })
        {
            if (args[key] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0)
            {
                hint = ": " + (s.Length > 60 ? s[..60] : s);
                break;
            }
        }

        return human.Length == 0 ? hint : char.ToUpperInvariant(human[0]) + human[1..] + hint;
    }
}
