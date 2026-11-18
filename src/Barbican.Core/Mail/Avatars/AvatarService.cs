// Mirrors: Rust avatars.rs (rules ported in AvatarRules; the worker: at most 4 fetches at once, a disk cache, the initials stay until the picture is
// ready), Bulwark lib/avatar-resolver.ts (BIMI for a DMARC-passing brand domain, Gravatar by SHA-256, the domain's favicon), lib/plugin-hook-catalog.ts:87
// onAvatarResolve (a plugin may name the URL). Off by default (AvatarPrefs). Every fetch goes through GuardedHttp (https only, public addresses, size
// cap, no redirects followed); the BIMI TXT record through the injected resolver (DnsClient in the app). A BIMI logo is SVG: without a rasterizer it
// counts as missing and the next source is used (open item). Owner reader.

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Barbican.Core.Mail.Reader.Hooks;
using Barbican.Core.Net;
using Barbican.Core.Plugins.Hooks;

namespace Barbican.Core.Mail.Avatars;

/// <summary>Resolves sender pictures in the background; <see cref="IAvatarService.TryGet"/> never waits.</summary>
public sealed class AvatarService(IGuardedHttp http, Func<AvatarPrefs> prefs, IHookBus hooks, TimeProvider time, Func<string, CancellationToken, Task<string?>>? txt, string? cacheDir) : IAvatarService
{
    private static readonly TimeSpan CacheAge = TimeSpan.FromDays(7);
    private readonly Lock _refreshLock = new();
    private long _generation;
    private readonly AvatarJobs _jobs = new();
    private readonly ConcurrentDictionary<string, AvatarSlot> _slots = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _owner = new(StringComparer.Ordinal);

    public event EventHandler<AvatarArrivedEventArgs>? Arrived;

    /// <summary>Fetches running and waiting (automation: avatar-state).</summary>
    public (int Busy, int Waiting, int Ready) State => (_jobs.Busy, _jobs.Waiting, _slots.Count(s => s.Value.State == AvatarSlotState.Ready));

    public AvatarImage? TryGet(string email, string? brandDomain, bool junk)
    {
        ArgumentNullException.ThrowIfNull(email);
        return AvatarRules.Get(email, brandDomain, prefs(), junk, key => _slots.GetValueOrDefault(key, AvatarSlot.Unknown), key => Request(key, email));
    }

    /// <summary>Clears cached pictures and drops results from work started before the refresh.</summary>
    public Task RefreshAsync(CancellationToken ct) => Task.Run(() => Refresh(ct), ct);

    private void Refresh(CancellationToken ct)
    {
        string[] owners;
        lock (_refreshLock)
        {
            ct.ThrowIfCancellationRequested();
            _generation++;
            _jobs.Clear();
            owners = [.. _owner.Values.Distinct(StringComparer.Ordinal)];
            _owner.Clear();
            _slots.Clear();
            if (cacheDir is not null && Directory.Exists(cacheDir))
            {
                foreach (var path in Directory.EnumerateFiles(cacheDir, "*.img", SearchOption.TopDirectoryOnly))
                {
                    ct.ThrowIfCancellationRequested();
                    File.Delete(path);
                }
            }
        }

        foreach (var owner in owners)
        {
            Arrived?.Invoke(this, new AvatarArrivedEventArgs(owner));
        }
    }

    private void Request(string key, string email)
    {
        lock (_refreshLock)
        {
            if (!_slots.TryAdd(key, AvatarSlot.Pending))
            {
                return;
            }

            _owner[key] = email;
            if (_jobs.Enqueue(key))
            {
                _ = Task.Run(WorkAsync);
            }
        }
    }

    private async Task WorkAsync()
    {
        while (true)
        {
            string? key;
            string email;
            long generation;
            lock (_refreshLock)
            {
                key = _jobs.NextJob();
                if (key is null)
                {
                    return;
                }

                generation = _generation;
                email = _owner.GetValueOrDefault(key) ?? string.Empty;
            }

            AvatarImage? image = null;
            try
            {
                image = await ResolveAsync(key, email, generation, CancellationToken.None).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // a failed picture is a missing one; the initials stay
            catch (Exception)
#pragma warning restore CA1031
            {
                image = null;
            }

            lock (_refreshLock)
            {
                if (generation != _generation)
                {
                    continue;
                }

                _slots[key] = image is null ? AvatarSlot.Missing : new AvatarSlot(AvatarSlotState.Ready, image);
            }

            if (image is not null)
            {
                Arrived?.Invoke(this, new AvatarArrivedEventArgs(email));
            }
        }
    }

    private async Task<AvatarImage?> ResolveAsync(string key, string email, long generation, CancellationToken ct)
    {
        var url = await UrlAsync(key, ct).ConfigureAwait(false);
        if (hooks.HasHandlers(HookNames.OnAvatarResolve))
        {
            var asked = await hooks.TransformAsync(HookNames.OnAvatarResolve, new AvatarRequest(email, null, url), ct).ConfigureAwait(false);
            url = asked.Url;
        }

        if (url is null || !AvatarRules.SafeHttpsHost(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        byte[]? bytes;
        lock (_refreshLock)
        {
            bytes = generation == _generation ? Cached(key) : null;
        }

        bytes ??= await FetchAsync(uri, ct).ConfigureAwait(false);
        if (bytes is null || AvatarRules.ToRaster(bytes) is not { } raster)
        {
            return null;
        }

        lock (_refreshLock)
        {
            if (generation != _generation)
            {
                return null;
            }

            Store(key, bytes);
        }

        return new AvatarImage(key, raster);
    }

    private async Task<string?> UrlAsync(string key, CancellationToken ct)
    {
        var colon = key.IndexOf(':', StringComparison.Ordinal);
        var (kind, value) = (key[..colon], key[(colon + 1)..]);
        switch (kind)
        {
            case "grav":
                return $"https://gravatar.com/avatar/{value}?s=96&d=404";
            case "fav":
                return $"https://{value}/favicon.ico";
            case "bimi" when txt is not null:
                var record = await txt($"default._bimi.{value}", ct).ConfigureAwait(false);
                return record is null ? null : AvatarRules.BimiLogoUrl(record);
            default:
                return null;
        }
    }

    private async Task<byte[]?> FetchAsync(Uri uri, CancellationToken ct)
    {
        var r = await http.SendAsync(new GuardedRequest(HttpMethod.Get, uri, Accept: "image/*"), ct).ConfigureAwait(false);
        return r.IsOk && r.Value!.StatusCode is >= 200 and < 300 && r.Value.Body.Length > 0 ? r.Value.Body.ToArray() : null;
    }

    private string? CachePath(string key) =>
        cacheDir is null ? null : Path.Combine(cacheDir, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32] + ".img");

    private byte[]? Cached(string key)
    {
        try
        {
            return CachePath(key) is { } path && File.Exists(path) && time.GetUtcNow().UtcDateTime - File.GetLastWriteTimeUtc(path) < CacheAge ? File.ReadAllBytes(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void Store(string key, byte[] bytes)
    {
        try
        {
            if (CachePath(key) is { } path && !File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }
        }
        catch (IOException)
        {
            // the picture stays in memory for this run
        }
        catch (UnauthorizedAccessException)
        {
            // the picture stays in memory for this run
        }
    }
}
