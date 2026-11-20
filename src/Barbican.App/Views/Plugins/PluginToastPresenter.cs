#if WINDOWS
using System.Text.Json;
using Barbican.App.Plugins;
using Barbican.App.Shell;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Notifications;
using Barbican.Core.Plugins.Runtime;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Common;

namespace Barbican.App.Views.Plugins;

internal sealed class PluginToastPresenter
{
    private readonly IDispatcher _dispatcher;
    private readonly IToastService _toasts;
    private readonly PluginPackageHost _packages;
    private readonly PluginProgressToasts _progress;

    public PluginToastPresenter(IDispatcher dispatcher, IToastService toasts, PluginPackageHost packages, PluginProgressToasts progress, TimeProvider time)
    {
        _dispatcher = dispatcher;
        _toasts = toasts;
        _packages = packages;
        _progress = progress;
        Methods = PluginToastMethods.Create(time, ShowAsync, ShowProgressAsync, DismissAsync);
    }

    internal IReadOnlyList<IPluginApiMethod> Methods { get; }

    private async Task<PluginErrorCode?> ShowAsync(PluginToast toast, IPluginInvocationContext context, CancellationToken ct)
    {
        var request = await RequestAsync(toast, context.Binding, ct).ConfigureAwait(false);
        if (request is null) return PluginErrorCode.StaleContext;
        return await TryApplyAsync(context, () =>
        {
            context.MarkMutationIssued();
            _ = _toasts.Show(request);
        }, ct).ConfigureAwait(false) ? null : PluginErrorCode.StaleContext;
    }

    private async Task<PluginToastProgressResult> ShowProgressAsync(PluginToast toast, IPluginInvocationContext context,
        Func<bool> takeBudget, CancellationToken ct)
    {
        var result = new PluginToastProgressResult(null, PluginErrorCode.StaleContext);
        var request = await RequestAsync(toast, context.Binding, ct).ConfigureAwait(false);
        if (request is not null)
            _ = await TryApplyAsync(context, () => result = _progress.Show(request, context, takeBudget), ct).ConfigureAwait(false);
        return result;
    }

    private async Task<PluginErrorCode?> DismissAsync(string id, IPluginInvocationContext context, CancellationToken ct) =>
        await TryApplyAsync(context, () => _progress.Dismiss(id, context), ct).ConfigureAwait(false) ? null : PluginErrorCode.StaleContext;

    private async Task<ToastRequest?> RequestAsync(PluginToast toast, PluginRuntimeBinding binding, CancellationToken ct)
    {
        var installed = await _packages.FindAsync(binding.Package.Id, ct).ConfigureAwait(false);
        if (!installed.IsOk || installed.Value is not { Enabled: true } item || item.Package.Identity != binding.Package)
            return null;

        var manifest = item.Package.Manifest;
        var name = manifest.TryGetProperty("name", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
        return new ToastRequest(toast.Message)
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
    }

    private async Task<bool> TryApplyAsync(IPluginInvocationContext context, Action apply, CancellationToken ct)
    {
        var binding = context.Binding;
        var shown = false;
        await _dispatcher.InvokeAsync(async () =>
        {
            if (ActiveWindow.Current is null || !await context.CheckCurrentAsync(ct).ConfigureAwait(true)) return;
            shown = await _packages.TryPostAsync(binding, grant =>
                !ct.IsCancellationRequested && _dispatcher.HasThreadAccess && ActiveWindow.Current is not null &&
                grant.Permissions.SetEquals(context.Grant.Permissions) && grant.AccountKeys.SetEquals(context.Grant.AccountKeys) &&
                grant.ApprovedHttpOrigins.SetEquals(context.Grant.ApprovedHttpOrigins) &&
                binding.TryWriteWhileAlive(apply), ct).ConfigureAwait(true);
        }).ConfigureAwait(false);
        return shown;
    }
}
#endif
