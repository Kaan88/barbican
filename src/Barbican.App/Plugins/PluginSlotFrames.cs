// Mirrors: Bulwark components/plugins/plugin-iframe-slot.tsx (one sandboxed frame per add-on slot, started with the slot's props and the host theme) and runtime.tsx:1495-1603 (the slot renderer).
// Barbican starts the frame as a Slot-kind runtime of its own through the same factory chain as the background runtime (same package checks, grant, revocation and API dispatcher), so a frame can never do more
// than the add-on's grant allows. The host asks for a slot only when the add-on's running background instance offered it at activation (Bulwark offersForSlot) and its grant holds the slot's permission.
using System.Text.Json;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
using Barbican.App.Plugins.Sandbox;

namespace Barbican.App.Plugins;

/// <summary>Starts the sandboxed frame of an add-on's slot into a panel of the app.</summary>
internal sealed class PluginSlotFrames(PluginPackageHost packages, PluginBackgroundRuntimes background) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Func<CancellationToken, Task<Result<IPluginRuntimeFactory>>>? _create;
    private IPluginRuntimeFactory? _factory;

    /// <summary>Gives the service the factory of visible frames; called once by the shell when the add-on host starts.</summary>
    internal void Initialize(Func<CancellationToken, Task<Result<IPluginRuntimeFactory>>> create) => Interlocked.CompareExchange(ref _create, create, null);

    /// <summary>Whether the add-on's offer of <paramref name="slot"/> fills its container; null when it offers none or is not running.</summary>
    internal bool? Offer(string packageId, string slot) =>
        background.ActivationOf(packageId) is { } activation && activation.Slots.FirstOrDefault(s => string.Equals(s.Name, slot, StringComparison.Ordinal)) is { } offer
            ? offer.Fill : null;

    /// <summary>Starts the frame. <paramref name="mount"/> receives the WebView; <paramref name="permission"/> is the add-on permission the slot needs.</summary>
    internal async Task<Result<IPluginSlotFrameInstance>> StartAsync(string packageId, string slot, string permission, PluginValue props,
        Microsoft.UI.Xaml.Controls.Panel mount, JsonElement theme, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(mount);
        if (Offer(packageId, slot) is not { } fill) return Fail("The add-on does not offer this slot.");
        var found = await packages.FindAsync(packageId, ct).ConfigureAwait(false);
        if (!found.IsOk || found.Value is not { Enabled: true, Error: null, Grant: { Enabled: true } grant } installation ||
            !grant.Permissions.Contains(permission)) return Fail("The add-on may not show this slot.");
        var factory = await FactoryAsync(ct).ConfigureAwait(false);
        if (factory is null) return Fail("Add-on frames are unavailable.");
#if WINDOWS
        PluginSlotMount.Set(mount);
#endif
        var result = await factory.StartAsync(background.SlotRequest(installation, slot, props, fill, theme), ct).ConfigureAwait(false);
        if (!result.IsOk) return Result<IPluginSlotFrameInstance>.Fail(result.Error!);
        if (result.Value is IPluginSlotFrameInstance frame) return Result<IPluginSlotFrameInstance>.Ok(frame);
        if (result.Value is { } other) await other.DisposeAsync().ConfigureAwait(false);
        return Fail("The add-on frame could not be drawn.");
    }

    private async Task<IPluginRuntimeFactory?> FactoryAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _factory) is { } known) return known;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_factory is not null) return _factory;
            if (Volatile.Read(ref _create) is not { } create) return null;
            var made = await create(ct).ConfigureAwait(false);
            if (made.IsOk) Volatile.Write(ref _factory, made.Value);
            return _factory;
        }
        finally { _gate.Release(); }
    }

    public void Dispose() => _gate.Dispose();

    private static Result<IPluginSlotFrameInstance> Fail(string message) => Result<IPluginSlotFrameInstance>.Fail(new ValidationError(message));
}
