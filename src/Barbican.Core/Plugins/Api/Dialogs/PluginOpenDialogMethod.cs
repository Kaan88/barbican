// Mirrors Bulwark lib/plugin-sandbox/host-api.ts:222 (ui.openDialog needs no method permission), :2287-2310 (opts { title?, slot, extraProps?, width?, fill?, anchor? }; "slot" is required; the dialog resolves to what the slot
// passes to its onResult prop, or null when the user closes it), host-dialog.ts:56-155 (one dialog per add-on, 20 a minute, width 280-1200, texts cut), runtime.tsx:800-845 (no timeout) and plugin-slot-catalog.ts:53 (the
// 'plugin-dialog' slot needs ui:observe, which the frame is held to).
// Barbican draws only the slots listed in Slots (the catalogue permission of each is demanded before anything opens: Bulwark leaves a slot the add-on may not show empty, Barbican refuses the call). `anchor` (a popover beside
// the control) and the slot's setDialogButtons footer are not drawn: the dialog is always centred and the frame draws its own buttons. A confirm or prompt cannot be stacked on the add-on's own dialog (one dialog per add-on).
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Dialogs;

/// <summary>ui.openDialog: shows one of the calling add-on's own slots in a dialog of the host and returns what the slot's onResult callback was given.</summary>
internal sealed class PluginOpenDialogMethod(PluginDialogMethods.Gate gate, TimeProvider time,
    Func<PluginSlotDialogRequest, IPluginInvocationContext, CancellationToken, Task<PluginSlotDialogOutcome>>? present) : IPluginApiMethod
{
    /// <summary>Bulwark MIN_DIALOG_WIDTH and MAX_DIALOG_WIDTH.</summary>
    internal const double MinWidth = 280, MaxWidth = 1200;

    /// <summary>The slots a dialog can draw, with the permission the catalogue gives each (plugin-slot-catalog.ts).</summary>
    internal static readonly IReadOnlyDictionary<string, string> Slots = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["plugin-dialog"] = "ui:observe",
    };

    // No method permission and no deadline: the call waits for the user (host-api.ts:222, runtime.tsx:841 callApi(..., 0)).
    public PluginApiDescriptor Descriptor { get; } = new("ui.openDialog", [], Timeout.InfiniteTimeSpan);

    /// <summary>Reads the call's one argument; null when it has no usable "slot" or names a slot this host does not draw.</summary>
    internal static PluginSlotDialogRequest? Read(PluginValue arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments is not { Kind: PluginValueKind.Array, Count: > 0 } || arguments.Item(0) is not { Kind: PluginValueKind.Object } options) return null;
        if (!options.TryGetProperty("slot", out var slot) || slot.Kind != PluginValueKind.String || !Slots.TryGetValue(slot.StringValue, out var permission)) return null;
        string? title = null;
        if (options.TryGetProperty("title", out var text) && text.Kind == PluginValueKind.String)
        {
            var clean = PluginDialogText.Clean(text.StringValue, PluginDialogText.MaxTitle, multiline: false);
            if (clean.Length > 0) title = clean;
        }

        double? width = null;
        if (options.TryGetProperty("width", out var size) && size.Kind == PluginValueKind.Number &&
            double.TryParse(size.NumberText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pixels) && double.IsFinite(pixels))
            width = Math.Clamp(pixels, MinWidth, MaxWidth);
        var fill = options.TryGetProperty("fill", out var wanted) && wanted is { Kind: PluginValueKind.Boolean, BooleanValue: true };
        var props = options.TryGetProperty("extraProps", out var extra) && extra.Kind == PluginValueKind.Object ? extra : Empty;
        return new PluginSlotDialogRequest(slot.StringValue, permission, title, props, width, fill);
    }

    public PluginErrorCode? ValidateArguments(PluginValue arguments) => Read(arguments) is null ? PluginErrorCode.InvalidArguments : null;

    public async Task<PluginCallOutcome> InvokeAsync(IPluginInvocationContext context, PluginValue arguments, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Read(arguments) is not { } request) return PluginCallOutcome.Failed(PluginErrorCode.InvalidArguments);
        if (present is null) return PluginCallOutcome.Failed(PluginErrorCode.Unavailable);
        // The slot's own permission, from the grant of this very runtime; a neighbouring one does not count.
        if (!context.Demand(request.Permission, null).IsOk) return PluginCallOutcome.Failed(PluginErrorCode.PermissionDenied);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return PluginCallOutcome.Failed(PluginErrorCode.StaleContext);
        var owner = (context.Binding.Profile, context.Binding.Package.Id);
        if (gate.TryOpen(owner) is { } refused) return PluginCallOutcome.Failed(refused);
        try
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var stale = 0;
            var watcher = Task.Run(() => PluginDialogMethods.Method.WatchAsync(context, stop, () => Interlocked.Exchange(ref stale, 1), time), CancellationToken.None);
            PluginSlotDialogOutcome outcome;
            try { outcome = await present(request, context, stop.Token).ConfigureAwait(false); }
            finally
            {
                await stop.CancelAsync().ConfigureAwait(false);
                await watcher.ConfigureAwait(false);
            }

            if (ct.IsCancellationRequested) return PluginCallOutcome.Failed(PluginErrorCode.Cancelled);
            if (outcome.Error is { } error) return PluginCallOutcome.Failed(error);
            if (Volatile.Read(ref stale) == 1 || !await context.CheckCurrentAsync(ct).ConfigureAwait(false))
                return PluginCallOutcome.Failed(PluginErrorCode.StaleContext);
            if (outcome.Result is { } result) return PluginCallOutcome.Succeeded(result);
            var builder = new PluginValueBuilder();
            return PluginCallOutcome.Succeeded(builder.Build(builder.Null()));
        }
        finally
        {
            gate.Close(owner);
        }
    }

    private static PluginValue Empty { get; } = MakeEmpty();

    private static PluginValue MakeEmpty()
    {
        var builder = new PluginValueBuilder();
        return builder.Build(builder.Object());
    }
}
