// Mirrors Bulwark components/plugins/plugin-dialog-host.tsx:117-160,306-325 (the host names the add-on in a line the add-on's text cannot change; title falls back to the add-on's name).
#if WINDOWS
using System.Text.Json;
using Barbican.App.Plugins;
using Barbican.App.Shell;
using Barbican.Core.Plugins.Api.Dialogs;
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

    public PluginDialogPresenter(IDispatcher dispatcher, IDialogService dialogs, PluginPackageHost packages, TimeProvider time)
    {
        _dispatcher = dispatcher;
        _dialogs = dialogs;
        _packages = packages;
        Methods = PluginDialogMethods.Create(time, ShowAsync);
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
        var hostRequest = new PluginDialogHostRequest(title, name, request);

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
}
#endif
