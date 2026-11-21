// Mirrors Bulwark components/plugins/plugin-dialog-host.tsx:117-160,306-325 (the host names the add-on in a line the add-on's text cannot change; title falls back to the add-on's name)
// and :229-240,353 (ui.openDialog: the add-on's own slot frame inside the dialog, with an onResult callback that answers this dialog only).
#if WINDOWS
using System.Text.Json;
using Barbican.App.Plugins;
using Barbican.App.Shell;
using Barbican.Core.Plugins.Api.Dialogs;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Notifications;
using Barbican.Presentation.Common;
using Barbican.Presentation.Dialogs;

namespace Barbican.App.Views.Plugins;

/// <summary>Draws ui.alert, ui.confirm and ui.prompt in the in-window dialog host, only while the add-on is enabled and the call is current.</summary>
internal sealed class PluginDialogPresenter
{
    private const int TitleDisplayLimit = 200;
    private readonly IDispatcher _dispatcher;
    private readonly IDialogService _dialogs;
    private readonly PluginPackageHost _packages;
    private readonly PluginSlotFrames _frames;

    public PluginDialogPresenter(IDispatcher dispatcher, IDialogService dialogs, PluginPackageHost packages, PluginSlotFrames frames, TimeProvider time)
    {
        _dispatcher = dispatcher;
        _dialogs = dialogs;
        _packages = packages;
        _frames = frames;
        Methods = PluginDialogMethods.Create(time, ShowAsync, ShowSlotAsync);
    }

    internal IReadOnlyList<IPluginApiMethod> Methods { get; }

    private async Task<PluginDialogOutcome> ShowAsync(PluginDialogRequest request, IPluginInvocationContext context, CancellationToken ct)
    {
        var binding = context.Binding;
        var installed = await _packages.FindAsync(binding.Package.Id, ct).ConfigureAwait(false);
        if (!installed.IsOk || installed.Value is not { Enabled: true } item || item.Package.Identity != binding.Package)
            return PluginDialogOutcome.Failure(PluginErrorCode.StaleContext);
        var manifest = item.Package.Manifest;
        var name = PluginToastMethods.SourceLabel(
            manifest.TryGetProperty("name", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null, binding.Package.Id);
        var title = request.Title ?? name;
        if (title.Length > TitleDisplayLimit) title = title[..(char.IsHighSurrogate(title[TitleDisplayLimit - 1]) ? TitleDisplayLimit - 1 : TitleDisplayLimit)] + "…";
        var hostRequest = new PluginDialogHostRequest(title, name, request, () => _packages.SetEnabledAsync(binding.Package, false, CancellationToken.None));

        var outcome = PluginDialogOutcome.Failure(PluginErrorCode.Unavailable);
        await _dispatcher.InvokeAsync(async () =>
        {
            if (ActiveWindow.Current is null) return;
            if (!await context.CheckCurrentAsync(ct).ConfigureAwait(true))
            {
                outcome = PluginDialogOutcome.Failure(PluginErrorCode.StaleContext);
                return;
            }

            // the dialog also ends with the frame: the lifetime of the binding closes it when the add-on is disabled, removed or its account goes away
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct, binding.Lifetime);
            Task<PluginDialogOutcome?>? dialog = null;
            var posted = await _packages.TryPostAsync(binding, grant =>
                !stop.IsCancellationRequested && ActiveWindow.Current is not null &&
                grant.Permissions.SetEquals(context.Grant.Permissions) && grant.AccountKeys.SetEquals(context.Grant.AccountKeys) &&
                binding.TryWriteWhileAlive(() => dialog = _dialogs.ShowAsync(hostRequest, stop.Token)), ct).ConfigureAwait(true);
            if (!posted || dialog is null)
            {
                outcome = PluginDialogOutcome.Failure(PluginErrorCode.StaleContext);
                return;
            }

#pragma warning disable VSTHRD003 // The dialog was started on this UI dispatcher just above.
            outcome = await dialog.ConfigureAwait(true) ?? PluginDialogOutcome.Cancelled;
#pragma warning restore VSTHRD003
        }).ConfigureAwait(false);
        return outcome;
    }

    // ui.openDialog (Bulwark host-api.ts:2287-2310): the add-on's own slot frame in a dialog; the slot's onResult callback answers it. The callbacks belong to this frame only and are cleared when the dialog ends,
    // so a second onResult after it closed answers nothing (host-dialog.ts resolveDialog).
    private async Task<PluginSlotDialogOutcome> ShowSlotAsync(PluginSlotDialogRequest request, IPluginInvocationContext context, CancellationToken ct)
    {
        var binding = context.Binding;
        var installed = await _packages.FindAsync(binding.Package.Id, ct).ConfigureAwait(false);
        if (!installed.IsOk || installed.Value is not { Enabled: true, Grant: { Enabled: true } grant } item || item.Package.Identity != binding.Package ||
            !grant.Permissions.Contains(request.Permission))
            return PluginSlotDialogOutcome.Failure(PluginErrorCode.StaleContext);
        // an add-on that is not running, or does not offer the slot, has nothing to draw
        if (_frames.Offer(binding.Package.Id, request.Slot) is null) return PluginSlotDialogOutcome.Failure(PluginErrorCode.Unavailable);
        var manifest = item.Package.Manifest;
        var name = PluginToastMethods.SourceLabel(
            manifest.TryGetProperty("name", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null, binding.Package.Id);
        var title = request.Title ?? name;
        if (title.Length > TitleDisplayLimit) title = title[..(char.IsHighSurrogate(title[TitleDisplayLimit - 1]) ? TitleDisplayLimit - 1 : TitleDisplayLimit)] + "…";

        var callbacks = new PluginHostCallbacks();
        var answered = new TaskCompletionSource<PluginValue>(TaskCreationOptions.RunContinuationsAsynchronously);
        var undefined = new PluginValueBuilder();
        var nothing = undefined.Build(undefined.Undefined());
        var onResult = callbacks.Register((arguments, stopped) =>
        {
            _ = answered.TrySetResult(arguments.Count > 0 ? arguments.Item(0) : nothing);
            return Task.FromResult<PluginValue?>(null);
        });
        var props = PluginSlotDialogProps.Build(request.ExtraProps, onResult);
        var hostRequest = new PluginSlotDialogHostRequest(title, name, request,
            (mount, theme, token) => _frames.StartAsync(binding.Package.Id, request.Slot, request.Permission, props, mount, theme, token, callbacks),
            () => _packages.SetEnabledAsync(binding.Package, false, CancellationToken.None));

        try
        {
            return await RunSlotDialogAsync(hostRequest, answered, context, ct).ConfigureAwait(false);
        }
        finally
        {
            callbacks.Clear();
        }
    }

    private async Task<PluginSlotDialogOutcome> RunSlotDialogAsync(PluginSlotDialogHostRequest hostRequest, TaskCompletionSource<PluginValue> answered,
        IPluginInvocationContext context, CancellationToken ct)
    {
        var binding = context.Binding;
        var outcome = PluginSlotDialogOutcome.Failure(PluginErrorCode.Unavailable);
        await _dispatcher.InvokeAsync(async () =>
        {
            if (ActiveWindow.Current is null) return;
            if (!await context.CheckCurrentAsync(ct).ConfigureAwait(true))
            {
                outcome = PluginSlotDialogOutcome.Failure(PluginErrorCode.StaleContext);
                return;
            }

            // the dialog also ends with the add-on: the lifetime of the binding closes it when the add-on is disabled, removed or its account goes away
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct, binding.Lifetime);
            Task<PluginSlotDialogOutcome?>? dialog = null;
            var posted = await _packages.TryPostAsync(binding, current =>
                !stop.IsCancellationRequested && ActiveWindow.Current is not null &&
                current.Permissions.SetEquals(context.Grant.Permissions) && current.AccountKeys.SetEquals(context.Grant.AccountKeys) &&
                binding.TryWriteWhileAlive(() => dialog = _dialogs.ShowAsync(hostRequest, stop.Token)), ct).ConfigureAwait(true);
            if (!posted || dialog is null)
            {
                outcome = PluginSlotDialogOutcome.Failure(PluginErrorCode.StaleContext);
                return;
            }

#pragma warning disable VSTHRD003 // The dialog was started on this UI dispatcher just above.
            var finished = await Task.WhenAny(dialog, answered.Task).ConfigureAwait(true);
            var slotAnswered = ReferenceEquals(finished, answered.Task) && answered.Task.IsCompletedSuccessfully;
            if (slotAnswered) await stop.CancelAsync().ConfigureAwait(true);
            _ = await dialog.ConfigureAwait(true);
            outcome = slotAnswered ? PluginSlotDialogOutcome.Answered(await answered.Task.ConfigureAwait(true)) : PluginSlotDialogOutcome.Closed;
#pragma warning restore VSTHRD003
        }).ConfigureAwait(false);
        return outcome;
    }
}
#endif
