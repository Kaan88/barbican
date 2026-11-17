// Mirrors: ADR 0008 decision 1 (Linux: Secret Service items with schema=barbican-next, profile, name); PORT-PLAN.md §3.9 (secrets row,
// Tmds.DBus); freedesktop.org Secret Service API 0.2 (org.freedesktop.Secret.Service OpenSession "plain", SearchItems, GetSecrets, Unlock;
// org.freedesktop.Secret.Collection CreateItem with replace; org.freedesktop.Secret.Item Delete; the default collection alias). Rust used
// the keyring crate (secret_service.rs: the same calls through libsecret's D-Bus API). A locked keyring is reported, never unlocked with a
// prompt: a prompt would put a window on the screen (M1-plan §1).
// Unverified on Linux at the time of writing: the VM was unreachable (FABLE-REVIEW "M1 stream: accounts").

using System.Security.Cryptography;
using System.Text;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Tmds.DBus.Protocol;

namespace Barbican.Platform.Linux;

/// <summary>Secrets in the desktop's Secret Service (GNOME Keyring, KWallet), under this profile's attributes.</summary>
public sealed class SecretServiceStore(string profile) : ISecretStore
{
    /// <summary>The schema attribute value.</summary>
    public const string Schema = "barbican-next";

    private const string Bus = "org.freedesktop.secrets";
    private const string ServicePath = "/org/freedesktop/secrets";
    private const string DefaultCollection = "/org/freedesktop/secrets/aliases/default";
    private const string ServiceInterface = "org.freedesktop.Secret.Service";

    /// <summary>The profile whose items this store reads and writes.</summary>
    public string Profile { get; } = profile;

    /// <inheritdoc/>
    public Task<Result<string?>> ReadAsync(SecretName name, CancellationToken ct) => RunAsync(async c =>
    {
        var session = await OpenSessionAsync(c).ConfigureAwait(false);
        var (unlocked, locked) = await SearchAsync(c, Attributes(name)).ConfigureAwait(false);
        if (unlocked.Length == 0)
        {
            return locked.Length == 0
                ? Result<string?>.Ok(null)
                : Result<string?>.Fail(new TransportError("The keyring is locked.", Retryable: true));
        }

        var bytes = await GetSecretAsync(c, unlocked[0], session).ConfigureAwait(false);
        try
        {
            return Result<string?>.Ok(bytes is null ? null : Encoding.UTF8.GetString(bytes));
        }
        finally
        {
            if (bytes is not null)
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
    });

    /// <inheritdoc/>
    public Task<Result<Unit>> WriteAsync(SecretName name, string value, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(value);
        return RunAsync(async c =>
        {
            var session = await OpenSessionAsync(c).ConfigureAwait(false);
            var bytes = Encoding.UTF8.GetBytes(value);
            try
            {
                await CreateItemAsync(c, session, $"Barbican ({Profile}): {name.Kind}", Attributes(name), bytes).ConfigureAwait(false);
                return Result<Unit>.Ok(default);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        });
    }

    /// <inheritdoc/>
    public Task<Result<Unit>> DeleteAsync(SecretName name, CancellationToken ct) => RunAsync(async c =>
    {
        var (unlocked, locked) = await SearchAsync(c, Attributes(name)).ConfigureAwait(false);
        foreach (var item in unlocked.Concat(locked))
        {
            await CallAsync(c, item, "org.freedesktop.Secret.Item", "Delete", null, null).ConfigureAwait(false);
        }

        return Result<Unit>.Ok(default);
    });

    /// <summary>The attributes of an item: schema, profile, name (and account when given).</summary>
    public IReadOnlyDictionary<string, string> Attributes(SecretName name)
    {
        var a = new Dictionary<string, string>(StringComparer.Ordinal) { ["schema"] = Schema, ["profile"] = Profile, ["name"] = name.Kind };
        if (name.Account is { Length: > 0 } account)
        {
            a["account"] = account;
        }

        return a;
    }

    private static async Task<Result<T>> RunAsync<T>(Func<DBusConnection, Task<Result<T>>> body)
    {
        if (DBusAddress.Session is not { Length: > 0 } address)
        {
            return Result<T>.Fail(new TransportError("No D-Bus session bus: the Secret Service is not available.", Retryable: false));
        }

        try
        {
            using var connection = new DBusConnection(address);
            await connection.ConnectAsync().ConfigureAwait(false);
            return await body(connection).ConfigureAwait(false);
        }
        catch (DBusExceptionBase e)
        {
            return Result<T>.Fail(new TransportError($"Secret Service: {e.Message}", Retryable: false));
        }
    }

    private static async Task CallAsync(DBusConnection c, string path, string iface, string member, string? signature, Action<MessageWriter>? body) =>
        await c.CallMethodAsync(Build(c, path, iface, member, signature, body)).ConfigureAwait(false);

    // MessageWriter is a ref struct: messages are built synchronously, then sent
    private static MessageBuffer Build(DBusConnection c, string path, string iface, string member, string? signature, Action<MessageWriter>? body)
    {
        using var writer = c.GetMessageWriter();
        writer.WriteMethodCallHeader(Bus, path, iface, member, signature, MessageFlags.None);
        body?.Invoke(writer);
        return writer.CreateMessage();
    }

    private static async Task<string> OpenSessionAsync(DBusConnection c)
    {
        var message = Build(c, ServicePath, ServiceInterface, "OpenSession", "sv", static w =>
        {
            w.WriteString("plain");
            w.WriteVariantString("");
        });
        return await c.CallMethodAsync(message, static (Message m, object? _) =>
        {
            var r = m.GetBodyReader();
            _ = r.ReadVariantValue();
            return r.ReadObjectPathAsString();
        }, null).ConfigureAwait(false);
    }

    private static async Task<(string[] Unlocked, string[] Locked)> SearchAsync(DBusConnection c, IReadOnlyDictionary<string, string> attributes)
    {
        var message = Build(c, ServicePath, ServiceInterface, "SearchItems", "a{ss}", w => WriteStringDictionary(w, attributes));
        return await c.CallMethodAsync(message, static (Message m, object? _) =>
        {
            var r = m.GetBodyReader();
            var unlocked = r.ReadArrayOfObjectPath().Select(p => p.ToString()).ToArray();
            var locked = r.ReadArrayOfObjectPath().Select(p => p.ToString()).ToArray();
            return (unlocked, locked);
        }, null).ConfigureAwait(false);
    }

    private static async Task<byte[]?> GetSecretAsync(DBusConnection c, string item, string session)
    {
        var message = Build(c, ServicePath, ServiceInterface, "GetSecrets", "aoo", w =>
        {
            w.WriteArray(new[] { new ObjectPath(item) });
            w.WriteObjectPath(session);
        });
        return await c.CallMethodAsync(message, static (Message m, object? _) =>
        {
            var r = m.GetBodyReader();
            var end = r.ReadDictionaryStart();
            byte[]? value = null;
            while (r.HasNext(end))
            {
                r.AlignStruct();
                _ = r.ReadObjectPathAsString();
                r.AlignStruct();
                _ = r.ReadObjectPathAsString();
                _ = r.ReadArrayOfByte();
                value ??= r.ReadArrayOfByte();
                _ = r.ReadString();
            }

            return value;
        }, null).ConfigureAwait(false);
    }

    private static Task CreateItemAsync(DBusConnection c, string session, string label, IReadOnlyDictionary<string, string> attributes, byte[] secret) =>
        CallAsync(c, DefaultCollection, "org.freedesktop.Secret.Collection", "CreateItem", "a{sv}(oayays)b", w =>
        {
            var props = w.WriteDictionaryStart();
            w.WriteDictionaryEntryStart();
            w.WriteString("org.freedesktop.Secret.Item.Label");
            w.WriteVariantString(label);
            w.WriteDictionaryEntryStart();
            w.WriteString("org.freedesktop.Secret.Item.Attributes");
            w.WriteSignature("a{ss}");
            WriteStringDictionary(w, attributes);
            w.WriteDictionaryEnd(props);
            w.WriteStructureStart();
            w.WriteObjectPath(session);
            w.WriteArray(ReadOnlySpan<byte>.Empty);
            w.WriteArray(secret);
            w.WriteString("text/plain; charset=utf8");
            w.WriteBool(true);
        });

    private static void WriteStringDictionary(MessageWriter w, IReadOnlyDictionary<string, string> values)
    {
        var start = w.WriteDictionaryStart();
        foreach (var (k, v) in values)
        {
            w.WriteDictionaryEntryStart();
            w.WriteString(k);
            w.WriteString(v);
        }

        w.WriteDictionaryEnd(start);
    }
}
