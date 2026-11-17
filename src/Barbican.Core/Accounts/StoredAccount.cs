// Mirrors: Rust main.rs:1946 remember_session and the sign-in handlers (the remembered object: kind, server, user, then pass for a password
// sign-in or refresh, endpoint and client for OAuth); ADR 0008 (the same object in the C# app's own entries, UTF-8 JSON). Keys this version
// does not know are kept (PORT-PLAN.md §3.5). New keys: readOnly (PORT-PLAN.md §4.6) and default (Bulwark stores/account-store.ts:162,
// the first account added is the default one).

using System.Text.Json.Nodes;

namespace Barbican.Core.Accounts;

/// <summary>A remembered account with its secret. <see cref="ToString"/> never prints the secret.</summary>
public sealed record StoredAccount(string Kind, string Server, string User)
{
    /// <summary>A password account.</summary>
    public const string PasswordKind = "password";

    /// <summary>An OAuth account.</summary>
    public const string OAuthKind = "oauth";

    private static readonly string[] Known = ["kind", "server", "user", "pass", "refresh", "endpoint", "client", "readOnly", "default"];

    /// <summary>The password (password kind).</summary>
    public string? Password { get; init; }

    /// <summary>The refresh token (OAuth kind).</summary>
    public string? Refresh { get; init; }

    /// <summary>The token endpoint (OAuth kind).</summary>
    public string? Endpoint { get; init; }

    /// <summary>The client id (OAuth kind).</summary>
    public string? Client { get; init; }

    /// <summary>Writes are refused for this account.</summary>
    public bool ReadOnly { get; init; }

    /// <summary>The default account (the star in the account popover).</summary>
    public bool IsDefault { get; init; }

    /// <summary>Members this version does not know, kept as they were.</summary>
    public JsonObject? Extra { get; init; }

    /// <summary>The account key.</summary>
    public string Key => AccountKey.Of(Server, User);

    /// <summary>True for the OAuth kind.</summary>
    public bool IsOAuth => string.Equals(Kind, OAuthKind, StringComparison.Ordinal);

    /// <summary>The account in a remembered JSON object, or null when kind, server or user is missing or the kind is unknown.</summary>
    public static StoredAccount? FromJson(JsonNode? node)
    {
        if (node is not JsonObject o)
        {
            return null;
        }

        string? Str(string k) => o[k] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;
        bool Flag(string k) => o[k] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
        var kind = Str("kind");
        var server = Str("server");
        var user = Str("user");
        if (kind is not (PasswordKind or OAuthKind) || server is null || user is null)
        {
            return null;
        }

        JsonObject? extra = null;
        foreach (var (k, v) in o)
        {
            if (!Known.Contains(k, StringComparer.Ordinal))
            {
                extra ??= [];
                extra[k] = v?.DeepClone();
            }
        }

        return new StoredAccount(kind, server, user)
        {
            Password = Str("pass"),
            Refresh = Str("refresh"),
            Endpoint = Str("endpoint"),
            Client = Str("client"),
            ReadOnly = Flag("readOnly"),
            IsDefault = Flag("default"),
            Extra = extra,
        };
    }

    /// <summary>The remembered JSON object (Rust's keys, plus readOnly and default when set, plus the kept unknown members).</summary>
    public JsonObject ToJson()
    {
        var o = new JsonObject { ["kind"] = Kind, ["server"] = Server, ["user"] = User };
        if (Password is not null)
        {
            o["pass"] = Password;
        }

        if (Refresh is not null)
        {
            o["refresh"] = Refresh;
        }

        if (Endpoint is not null)
        {
            o["endpoint"] = Endpoint;
        }

        if (Client is not null)
        {
            o["client"] = Client;
        }

        if (ReadOnly)
        {
            o["readOnly"] = true;
        }

        if (IsDefault)
        {
            o["default"] = true;
        }

        foreach (var (k, v) in Extra ?? [])
        {
            o[k] = v?.DeepClone();
        }

        return o;
    }

    /// <summary>Never prints the password or the refresh token.</summary>
    public override string ToString() => $"StoredAccount {{ Kind = {Kind}, User = {User}, Server = {Server}, ReadOnly = {ReadOnly} }}";

    /// <summary>The public part.</summary>
    public AccountInfo ToInfo(bool readOnly) => new(Key, User, Server) { Kind = Kind, IsReadOnly = readOnly };
}
