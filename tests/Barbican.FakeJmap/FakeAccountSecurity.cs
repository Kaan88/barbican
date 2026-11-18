// The fake account's security objects (Stalwart's management objects under urn:stalwart:jmap, reached with the user's own credentials: x:AccountPassword (singleton; otpAuth.otpUrl when two-step sign-in is on), x:AccountSettings (singleton: description, encryptionAtRest), x:AppPassword and x:ApiKey (query, get, set), x:PublicKey (set); Stalwart crates/jmap/src/registry/mapping/,
// crates/registry/src/schema/structs.rs; Bulwark stores/account-security-store.ts:220-403, lib/stalwart/principal.ts:34-59). A test hands one to FakeJmapOptions.AccountSecurity; the arms are in FakeApi.AccountSecurity.cs (srv-data does not touch them: set-data owns them). A password is kept only as a hash and never echoed. M6 plan section 4.12. Owner set-data.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

/// <summary>An in-memory account-security state.</summary>
public sealed class FakeAccountSecurity
{
    private byte[] _passwordHash = Hash("fake-current-password");

    /// <summary>The otpauth URL while two-step sign-in is on; null when off.</summary>
    public string? OtpUrl { get; set; }

    /// <summary>The account description (the display name Stalwart seeds the first identity from).</summary>
    public string Description { get; set; } = "Test User";

    /// <summary>The @type of encryptionAtRest: Disabled, Enabled or Aes256.</summary>
    public string EncryptionType { get; set; } = "Disabled";

    /// <summary>App passwords as the server lists them: id, description, createdAt, expiresAt (the secret is never listed).</summary>
    public IList<JsonObject> AppPasswords { get; } = [];

    /// <summary>API keys as the server lists them: id, description, expiresAt.</summary>
    public IList<JsonObject> ApiKeys { get; } = [];

    /// <summary>Public keys created for encryption at rest (id, description, key).</summary>
    public IList<JsonObject> PublicKeys { get; } = [];

    /// <summary>The password the fake account has (a test password; set it to make a change succeed or fail).</summary>
    public void SetPassword(string password) => _passwordHash = Hash(password);

    /// <summary>Whether the string is the current password.</summary>
    public bool IsPassword(string? candidate) => candidate is not null && CryptographicOperations.FixedTimeEquals(_passwordHash, Hash(candidate));

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}
