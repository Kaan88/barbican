// Mirrors: ADR 0008 decision 2 and "M0 review" (read accounts.Barbican, else session.Barbican; decode UTF-16LE; map kind/server/user and
// pass or refresh/endpoint/client; write only new entries; never modify or delete the Rust entries; skip an odd-length or non-JSON blob and
// report which one; zero the buffers; no values in logs); M1-plan §1 rule 3 and §6.3 (the agent path into dev / auto-<port> profiles takes
// password entries only and marks them read-only: an imported OAuth refresh token would rotate on first use and sign the Rust app out,
// B-OAUTH-015). The admin and assistant keys are imported only by the user (CLAUDE.md: agents never use the admin key).

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Storage;

namespace Barbican.Platform;

/// <summary>The one-time, read-only import of the Rust app's remembered accounts into this profile.</summary>
public sealed class RustCredentialImport(ILegacyCredentialSource legacy, ISecretStore store, IProfilePaths paths) : ILegacyAccountImport
{
    /// <summary>The Rust app's account list (keyring user "accounts", service "Barbican").</summary>
    public const string AccountsTarget = "accounts.Barbican";

    /// <summary>The Rust app's last session.</summary>
    public const string SessionTarget = "session.Barbican";

    /// <summary>The Rust app's admin key.</summary>
    public const string AdminKeyTarget = "stalwart-admin-key.Barbican";

    /// <summary>The Rust app's assistant key.</summary>
    public const string AiKeyTarget = "ai-api-key.Barbican";

    private static readonly UnicodeEncoding Utf16 = new(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);

    /// <inheritdoc/>
    public async Task<ImportReport> ImportAsync(ImportMode mode, CancellationToken ct)
    {
        var agentProfile = string.Equals(paths.Profile, ProfilePaths.DefaultProfile, StringComparison.Ordinal) || ProfilePaths.IsAutomationProfile(paths.Profile);
        if (mode == ImportMode.Agent && !agentProfile)
        {
            return new ImportReport(0, 0, ["The agent import runs only in the dev or an automation profile."]);
        }

        if (mode == ImportMode.User && agentProfile)
        {
            return new ImportReport(0, 0, ["The import is offered only in the default profile."]);
        }

        var skipped = new List<string>();
        var (source, nodes) = ReadEntries(skipped);
        var vault = new SecretAccountVault(store);
        var loaded = await vault.LoadAsync(ct).ConfigureAwait(false);
        if (!loaded.IsOk)
        {
            return new ImportReport(0, 0, [$"The profile's accounts could not be read: {loaded.Error!.Message}"]);
        }

        var list = loaded.Value!.ToList();
        var (imported, present) = Merge(list, source, nodes, mode, skipped);
        if (imported > 0)
        {
            var saved = await vault.SaveAsync(list, ct).ConfigureAwait(false);
            if (!saved.IsOk)
            {
                return new ImportReport(0, present, [.. skipped, $"The accounts could not be saved: {saved.Error!.Message}"]);
            }
        }

        if (mode == ImportMode.User)
        {
            await CopyKeyAsync(AdminKeyTarget, SecretTarget.AdminKey, skipped, ct).ConfigureAwait(false);
            await CopyKeyAsync(AiKeyTarget, SecretTarget.AiKey, skipped, ct).ConfigureAwait(false);
        }

        return new ImportReport(imported, present, skipped);
    }

    private static bool IsSession(string target) => string.Equals(target, SessionTarget, StringComparison.Ordinal);

    // adds the new accounts to the list; returns how many were added and how many the profile already had
    private static (int Imported, int Present) Merge(List<StoredAccount> list, string source, IReadOnlyList<JsonNode?> nodes, ImportMode mode, List<string> skipped)
    {
        int imported = 0, present = 0;
        for (var i = 0; i < nodes.Count; i++)
        {
            var name = IsSession(source) ? source : $"{source}[{i}]";
            if (StoredAccount.FromJson(nodes[i]) is not { } account)
            {
                skipped.Add($"{name}: not an account");
                continue;
            }

            if (mode == ImportMode.Agent && account.IsOAuth)
            {
                skipped.Add($"{name} (oauth): sign in by hand");
                continue;
            }

            if (list.Exists(a => string.Equals(a.Key, account.Key, StringComparison.Ordinal)))
            {
                present++;
                continue;
            }

            list.Add(account with { ReadOnly = mode == ImportMode.Agent || account.ReadOnly, IsDefault = list.TrueForAll(a => !a.IsDefault) });
            imported++;
        }

        return (imported, present);
    }

    /// <summary>
    /// The text of a blob: UTF-16LE without a terminator; null (with the reason added to <paramref name="skipped"/>) for an odd length or
    /// invalid UTF-16. The blob is zeroed either way.
    /// </summary>
    public static string? Decode(byte[] blob, string name, ICollection<string> skipped)
    {
        ArgumentNullException.ThrowIfNull(blob);
        ArgumentNullException.ThrowIfNull(skipped);
        try
        {
            if (blob.Length % 2 != 0)
            {
                skipped.Add($"{name}: odd-length blob, not UTF-16");
                return null;
            }

            return Utf16.GetString(blob);
        }
        catch (DecoderFallbackException)
        {
            skipped.Add($"{name}: not UTF-16");
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(blob);
        }
    }

    // the account objects of accounts.Barbican (an array), else of session.Barbican (one object)
    private (string Source, IReadOnlyList<JsonNode?> Nodes) ReadEntries(List<string> skipped)
    {
        foreach (var target in (string[])[AccountsTarget, SessionTarget])
        {
            if (legacy.Read(target) is not { } blob)
            {
                continue;
            }

            if (Decode(blob, target, skipped) is not { } text)
            {
                continue;
            }

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(text);
            }
            catch (JsonException)
            {
                skipped.Add($"{target}: not JSON");
                continue;
            }

            switch (node)
            {
                case JsonArray a when !IsSession(target):
                    return (target, [.. a]);
                case JsonObject o when IsSession(target):
                    return (target, [o]);
                default:
                    skipped.Add($"{target}: not an account {(IsSession(target) ? "object" : "list")}");
                    break;
            }
        }

        return (AccountsTarget, []);
    }

    private async Task CopyKeyAsync(string target, Barbican.Platform.SecretName name, List<string> skipped, CancellationToken ct)
    {
        if (legacy.Read(target) is not { } blob || Decode(blob, target, skipped) is not { Length: > 0 } key)
        {
            return;
        }

        var existing = await store.ReadAsync(name, ct).ConfigureAwait(false);
        if (existing.IsOk && existing.Value is null)
        {
            _ = await store.WriteAsync(name, key.Trim(), ct).ConfigureAwait(false);
        }
    }
}
