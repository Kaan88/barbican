// Not Rust rows (assistant.md: no Rust tests exist for ai.rs): B-AI-003, status mapping of Rust ai.rs. M6 stream set-data.

using Barbican.Core.Settings.Assistant;

namespace Barbican.Core.Tests.Settings.Assistant;

public sealed class AssistantErrorsTests
{
    [Fact]
    [Red("set-data")]
    public void Statuses_map_to_plain_messages()
    {
        Assert.Contains("key", AssistantErrors.Message(401, null, null), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("denied", AssistantErrors.Message(403, null, null), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not found", AssistantErrors.Message(404, null, null), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("30", AssistantErrors.Message(429, TimeSpan.FromSeconds(30), null), StringComparison.Ordinal);
        Assert.Contains("rate", AssistantErrors.Message(429, null, null), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("overloaded", AssistantErrors.Message(529, null, null), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("overloaded", AssistantErrors.Message(503, null, null), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bad shape", AssistantErrors.Message(400, null, "bad shape"), StringComparison.Ordinal);
    }
}
