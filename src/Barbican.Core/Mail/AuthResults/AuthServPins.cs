// Mirrors: Fable review 2 R2-07 ("learn and pin that id per account"); RFC 8601 §5. The first authserv-id found in a server trace block under a
// host the account trusts (AuthTrace) is kept for that account in authserv-ids.json in the profile's data folder; later messages count only
// results written under that id. The file is read once on the thread pool (no file IO on the UI thread); until then nothing is pinned or
// learned and the host rule of AuthServTrust decides. Owner reader.

using System.Text.Json;
using Barbican.Core.Storage;

namespace Barbican.Core.Mail.AuthResults;

/// <summary>The pinned authserv-id of each account.</summary>
public sealed class AuthServPins
{
    private readonly string? _path;
    private readonly Lock _lock = new();
    private readonly Task _loaded;
    private Dictionary<string, string> _pins = new(StringComparer.Ordinal);

    /// <summary>Pins kept in <c>authserv-ids.json</c> under <paramref name="dataDir"/>; null keeps them in memory only (tests).</summary>
    public AuthServPins(string? dataDir)
    {
        _path = dataDir is null ? null : Path.Combine(dataDir, "authserv-ids.json");
        _loaded = _path is null ? Task.CompletedTask : Task.Run(Load);
    }

    /// <summary>True once the pins file was read.</summary>
    public bool IsLoaded => _loaded.IsCompleted;

    /// <summary>The pinned id of an account, or null before one was learned.</summary>
    public string? PinFor(string? accountKey)
    {
        if (string.IsNullOrEmpty(accountKey))
        {
            return null;
        }

        if (!_loaded.IsCompleted)
        {
            return null;
        }

        lock (_lock)
        {
            return _pins.GetValueOrDefault(accountKey);
        }
    }

    /// <summary>Pins <paramref name="authServId"/> for an account unless one is pinned already (the first one stays).</summary>
    public void Learn(string? accountKey, string? authServId)
    {
        if (string.IsNullOrEmpty(accountKey) || string.IsNullOrWhiteSpace(authServId) || !_loaded.IsCompleted)
        {
            return;
        }

        string? text = null;
        lock (_lock)
        {
            var pins = _pins;
            if (pins.ContainsKey(accountKey))
            {
                return;
            }

            pins[accountKey] = authServId.Trim().ToLowerInvariant();
            if (_path is not null)
            {
                text = JsonSerializer.Serialize(pins, AuthServPinsJson.Default.DictionaryStringString);
            }
        }

        if (text is not null && _path is not null)
        {
            var path = _path;
            _ = Task.Run(() =>
            {
                try
                {
                    AtomicFile.WriteAllText(path, text);
                }
                catch (IOException)
                {
                    // the pin stays in memory for this run
                }
                catch (UnauthorizedAccessException)
                {
                    // the pin stays in memory for this run
                }
            });
        }
    }

    private void Load()
    {
        var read = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            if (_path is not null && AtomicFile.ReadAllTextOrNull(_path) is { } text)
            {
                foreach (var (k, v) in JsonSerializer.Deserialize(text, AuthServPinsJson.Default.DictionaryStringString) ?? [])
                {
                    read[k] = v;
                }
            }
        }
        catch (JsonException)
        {
            // a broken file pins nothing; the next learned id rewrites it
        }
        catch (IOException)
        {
            // unreadable: nothing pinned this run
        }

        lock (_lock)
        {
            _pins = read;
        }
    }
}
