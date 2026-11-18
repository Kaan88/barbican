// Mirrors: Rust files.rs:204-:215 (the primary account of the filenode capability, then every account that has it; draft 18 when supportedNodeTypes is listed on the account or the session), :213 legacy (rights named mayRead / mayWrite
// before Stalwart 0.16.6: an account capability without forbiddenNameChars); RFC 8620 section 2 (accounts, accountCapabilities); Bulwark lib/jmap/client.ts usesDraftFileNodeCreate. Owner files-data.

using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Jmap;

namespace Barbican.Core.Files;

/// <summary>What the session says about file storage.</summary>
internal static class FileAccounts
{
    public const string Capability = "urn:ietf:params:jmap:filenode";

    /// <summary>The user's own file account (the primary account of the capability, else the mail account).</summary>
    public static string Primary(JmapSession session) => session.AccountFor(Capability);

    /// <summary>The primary file account, then every other account that carries the capability.</summary>
    public static IReadOnlyList<string> All(JmapSession session)
    {
        var primary = Primary(session);
        var ids = new List<string> { primary };
        if (session.Raw.ValueKind == JsonValueKind.Object && session.Raw.TryGetProperty("accounts", out var accounts) && accounts.ValueKind == JsonValueKind.Object)
        {
            foreach (var a in accounts.EnumerateObject())
            {
                if (!string.Equals(a.Name, primary, StringComparison.Ordinal) && a.Value.ValueKind == JsonValueKind.Object
                    && a.Value.TryGetProperty("accountCapabilities", out var caps) && caps.ValueKind == JsonValueKind.Object && caps.TryGetProperty(Capability, out _))
                {
                    ids.Add(a.Name);
                }
            }
        }

        return ids;
    }

    /// <summary>The capability object of an account as JSON (account level, else session level), or null.</summary>
    public static JsonNode? CapabilityOf(JmapSession session, string accountId) =>
        session.CapabilityObject(accountId, Capability) is { } e ? JsonNode.Parse(e.GetRawText()) : null;

    /// <summary>Stalwart 1.0 (File Storage draft 18) lists supportedNodeTypes and wants @type on create.</summary>
    public static bool IsDraft(JmapSession session, string accountId) => CapabilityOf(session, accountId) is JsonObject o && o.ContainsKey("supportedNodeTypes");

    /// <summary>A server before 0.16.6 names the rights mayRead / mayWrite: its capability has no forbiddenNameChars.</summary>
    public static bool IsLegacy(JmapSession session, string accountId) => CapabilityOf(session, accountId) is JsonObject o && !o.ContainsKey("forbiddenNameChars");
}
