// Mirrors: Bulwark lib/plugin-sandbox/host-api-account.ts:226-270 (MAX_PLUGIN_DATA_BYTES 4096, 30 reads and 20 writes a minute, pluginDataKey = plugin.<id lower-cased> checked with isValidMetadataKey) over lib/jmap/bulwark-metadata.ts:42-89
// (KEY_RE, namespaceFor org.bulwarkmail.<key>, metadataValueBytes = the JSON in UTF-8). The add-on's data is one entry of the Inbox metadata of the mail account (urn:ietf:params:jmap:metadata; Stalwart crates/jmap/src/mailbox/set.rs:
// a "metadata/<namespace>" patch, null removes it). The key comes from the add-on's own id as the host knows it, never from an argument, so an add-on reaches only its own entry.
// Deviation: only the login's own mail account holds add-on data (Bulwark also names shared mail accounts, whose Inbox metadata Barbican's metadata writer does not reach): any other account id is Unavailable.
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Barbican.Core.Jmap;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Settings.Folders;

namespace Barbican.Core.Plugins.Api.AccountSettings;

internal static partial class PluginAccountData
{
    internal const int MaxBytes = 4096;
    internal const string Namespace = "org.bulwarkmail.";

    /// <summary>The metadata key of an add-on's data, or null when its id cannot be a metadata key (lowercase letters, digits, dashes and dots, at most 120 characters).</summary>
    internal static string? KeyOf(string pluginId)
    {
        var key = "plugin." + pluginId.ToLowerInvariant();
        return key.Length <= 120 && KeyShape().IsMatch(key) ? key : null;
    }

    /// <summary>The account argument: absent for the login's own mail account, otherwise an id; false when it is not an id.</summary>
    internal static bool TryAccountArgument(PluginValue? value, out string? accountId) => PluginMailArgs.TryAccount(value, out accountId);

    /// <summary>Whether the call names the login's own mail account (or none).</summary>
    internal static bool IsOwnAccount(IJmapClient client, string? accountId) => accountId is null || string.Equals(accountId, client.Session.MailAccountId, StringComparison.Ordinal);

    /// <summary>A JSON node as an add-on value (an entry the server holds, an administrator setting).</summary>
    internal static PluginValue ToPluginValue(JsonNode node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString());
        return PluginValue.FromJson(document.RootElement);
    }

    /// <summary>Whether the server keeps Inbox metadata at all.</summary>
    internal static bool Supported(IJmapClient client) => client.Session.Has(FolderAdminMetadata.Capability);

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]*(\.[a-z0-9][a-z0-9-]*)*$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 250)]
    private static partial Regex KeyShape();
}
