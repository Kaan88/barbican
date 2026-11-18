// Mirrors: Bulwark components/trusted-senders-modal.tsx and stores/settings-store.ts trustedSenders (addresses whose remote content loads), lib/sender-icon-access.ts;
// Rust reader.rs trust (per-sender allow, settings "trusted senders"); I-209. Kept locally in trusted-senders.json under the profile (D-3: no roaming),
// lower-cased, read on the thread pool and written atomically. Owner reader.

using System.Text.Json;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Core.Storage;

namespace Barbican.Core.Mail.Trust;

/// <summary>The senders whose remote content the user allowed.</summary>
public sealed class TrustedSenders : ITrustedSenders
{
    private readonly string? _path;
    private readonly IHookBus? _hooks;
    private readonly Lock _lock = new();
    private readonly Task _loaded;
    private List<string> _all = [];

    /// <summary>Kept in <c>trusted-senders.json</c> under <paramref name="dataDir"/>; null keeps them in memory only (tests).</summary>
    public TrustedSenders(string? dataDir, IHookBus? hooks = null)
    {
        _hooks = hooks;
        _path = dataDir is null ? null : Path.Combine(dataDir, "trusted-senders.json");
        _loaded = _path is null ? Task.CompletedTask : Task.Run(Load);
    }

    /// <summary>True once the file was read.</summary>
    public bool IsLoaded => _loaded.IsCompleted;

    public IReadOnlyList<string> All
    {
        get
        {
            lock (_lock)
            {
                return [.. _all];
            }
        }
    }

    public bool IsTrusted(string address)
    {
        if (string.IsNullOrWhiteSpace(address) || !_loaded.IsCompleted)
        {
            return false;
        }

        var key = address.Trim().ToLowerInvariant();
        lock (_lock)
        {
            return _all.Contains(key, StringComparer.Ordinal);
        }
    }

    public async Task<Result<Unit>> AddAsync(string address, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(address) || !address.Contains('@', StringComparison.Ordinal))
        {
            return Result<Unit>.Fail(new ValidationError("Not an e-mail address."));
        }

        await LoadedAsync().ConfigureAwait(false);
        var key = address.Trim().ToLowerInvariant();
        var added = await ChangeAsync(list => { if (!list.Contains(key, StringComparer.Ordinal)) { list.Add(key); } }, ct).ConfigureAwait(false);
        if (added.IsOk)
        {
            _hooks?.Fire(HookNames.OnTrustedSenderChange, () => new TrustedSenderChangePayload(key, true));
        }

        return added;
    }

    public async Task<Result<Unit>> RemoveAsync(string address, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(address);
        await LoadedAsync().ConfigureAwait(false);
        var key = address.Trim().ToLowerInvariant();
        var removed = await ChangeAsync(list => list.RemoveAll(a => string.Equals(a, key, StringComparison.Ordinal)), ct).ConfigureAwait(false);
        if (removed.IsOk)
        {
            _hooks?.Fire(HookNames.OnTrustedSenderChange, () => new TrustedSenderChangePayload(key, false));
        }

        return removed;
    }

#pragma warning disable VSTHRD003 // _loaded was started by the constructor on the thread pool; waiting for it needs no context
    private Task LoadedAsync() => _loaded;
#pragma warning restore VSTHRD003

    private async Task<Result<Unit>> ChangeAsync(Action<List<string>> change, CancellationToken ct)
    {
        string text;
        lock (_lock)
        {
            var next = new List<string>(_all);
            change(next);
            _all = next;
            text = JsonSerializer.Serialize(next, TrustedSendersJson.Default.ListString);
        }

        if (_path is null)
        {
            return Result<Unit>.Ok(default);
        }

        var path = _path;
        try
        {
            await Task.Run(() => AtomicFile.WriteAllText(path, text), ct).ConfigureAwait(false);
            return Result<Unit>.Ok(default);
        }
        catch (IOException e)
        {
            return Result<Unit>.Fail(new ValidationError("Could not save the trusted senders: " + e.Message));
        }
        catch (UnauthorizedAccessException e)
        {
            return Result<Unit>.Fail(new ValidationError("Could not save the trusted senders: " + e.Message));
        }
    }

    private void Load()
    {
        var read = new List<string>();
        try
        {
            if (_path is not null && AtomicFile.ReadAllTextOrNull(_path) is { } text)
            {
                read.AddRange((JsonSerializer.Deserialize(text, TrustedSendersJson.Default.ListString) ?? []).Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal));
            }
        }
        catch (JsonException)
        {
            // a broken file trusts nobody; the next change rewrites it
        }
        catch (IOException)
        {
            // unreadable: nobody trusted this run
        }

        lock (_lock)
        {
            _all = read;
        }
    }
}
