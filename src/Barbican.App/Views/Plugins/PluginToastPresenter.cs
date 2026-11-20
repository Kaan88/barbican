#if WINDOWS
using System.Text.Json;
using Barbican.App.Plugins;
using Barbican.App.Shell;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Notifications;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Common;

namespace Barbican.App.Views.Plugins;

internal sealed class PluginToastPresenter
{
    private readonly IDispatcher _dispatcher;
    private readonly IToastService _toasts;
    private readonly PluginPackageHost _packages;

    public PluginToastPresenter(IDispatcher dispatcher, IToastService toasts, PluginPackageHost packages, TimeProvider time)
    {
        _dispatcher = dispatcher;
        _toasts = toasts;
        _packages = packages;
        Methods = PluginToastMethods.Create(time, ShowAsync);
    }

    internal IReadOnlyList<IPluginApiMethod> Methods { get; }

    private async Task<PluginErrorCode?> ShowAsync(PluginToast toast, IPluginInvocationContext context, CancellationToken ct)
    {
        var binding = context.Binding;
        var installed = await _packages.FindAsync(binding.Package.Id, ct).ConfigureAwait(false);
        if (!installed.IsOk || installed.Value is not { Enabled: true } item || item.Package.Identity != binding.Package)
            return PluginErrorCode.StaleContext;

        var manifest = item.Package.Manifest;
        var name = manifest.TryGetProperty("name", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
        var request = new ToastRequest(toast.Message)
        {
            Source = PluginToastMethods.SourceLabel(name, binding.Package.Id),
            SourceId = binding.Package.Id,
            Severity = toast.Kind switch
            {
                PluginToastKind.Success => ToastSeverity.Success,
                PluginToastKind.Error => ToastSeverity.Error,
                PluginToastKind.Warning => ToastSeverity.Warning,
                _ => ToastSeverity.Info,
            },
        };
        var shown = false;
        await _dispatcher.InvokeAsync(async () =>
        {
            if (ActiveWindow.Current is null || !await context.CheckCurrentAsync(ct).ConfigureAwait(true)) return;
            shown = await _packages.TryPostAsync(binding, grant =>
                !ct.IsCancellationRequested && _dispatcher.HasThreadAccess && ActiveWindow.Current is not null &&
                grant.Permissions.SetEquals(context.Grant.Permissions) && grant.AccountKeys.SetEquals(context.Grant.AccountKeys) &&
                grant.ApprovedHttpOrigins.SetEquals(context.Grant.ApprovedHttpOrigins) &&
                binding.TryWriteWhileAlive(() =>
                {
                    context.MarkMutationIssued();
                    _ = _toasts.Show(request);
                }), ct).ConfigureAwait(true);
        }).ConfigureAwait(false);
        return shown ? null : PluginErrorCode.StaleContext;
    }
}
#endif
