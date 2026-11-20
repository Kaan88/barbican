#if WINDOWS
using System.Globalization;
using System.Text.Json;
using Barbican.App.Plugins;
using Barbican.App.Shell;
using Barbican.Core.OrganizationPolicy;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Notifications;
using Barbican.Core.Plugins.Runtime;
using Barbican.Presentation.Common;

namespace Barbican.App.Views.Plugins;

internal sealed class PluginPrintPresenter(IDispatcher dispatcher, OrganizationPrintPolicy policy, PluginPackageHost packages, TimeProvider time)
{
    private int _preparing;

    internal async Task<PluginErrorCode?> ShowAsync(PluginPrintRequest request, IPluginInvocationContext context,
        Func<bool> takeBudget, CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _preparing, 1, 0) != 0) return PluginErrorCode.Unavailable;
        try
        {
            // Count preparation attempts before policy requests so untrusted frames cannot flood providers.
            if (!takeBudget()) return PluginErrorCode.RateLimited;
            var authorization = await policy.ReadAsync(ct).ConfigureAwait(false);
            if (authorization is null) return PluginErrorCode.PermissionDenied;
            var title = request.Title ?? await TitleAsync(context, ct).ConfigureAwait(false);
            if (title is null) return PluginErrorCode.StaleContext;
            return await PrepareAsync(request, title, authorization, context, ct).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _preparing, 0);
        }
    }

    private async Task<PluginErrorCode?> PrepareAsync(PluginPrintRequest request, string title,
        OrganizationPrintPolicy.Authorization initial, IPluginInvocationContext context, CancellationToken ct)
    {
        PluginErrorCode? error = PluginErrorCode.Unavailable;
        var address = initial.WatermarkAddress;
        var watermark = address is null ? null :
            address + " · " + time.GetUtcNow().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
        await dispatcher.InvokeAsync(async () =>
        {
            if (ActiveWindow.Current is not { } window) return;
            Func<Func<bool>, bool>? continuation = null;
            bool ContinueWhileCurrent(Func<bool> useSource) => Volatile.Read(ref continuation)?.Invoke(useSource) == true;
            async Task<bool> IssueAsync(Func<bool> issue, CancellationToken token)
            {
                var latest = await policy.ReadAsync(token).ConfigureAwait(true);
                if (latest is null || !string.Equals(latest.WatermarkAddress, address, StringComparison.Ordinal))
                {
                    error = PluginErrorCode.PermissionDenied;
                    return false;
                }

                bool UseWhileCurrent(Func<bool> useSource) => !token.IsCancellationRequested && ReferenceEquals(ActiveWindow.Current, window) &&
                    initial.TryRunWhileCurrent(() => latest.TryRunWhileCurrent(() =>
                        PluginRuntimeBinding.TryWriteWhileAllAlive([context.Binding], useSource)));
                var issued = await ApplyAsync(window, context, UseWhileCurrent, continuePost =>
                {
                    Volatile.Write(ref continuation, useSource => continuePost(() => UseWhileCurrent(useSource)));
                    return issue();
                }, token).ConfigureAwait(true);
                if (!issued) error = PluginErrorCode.StaleContext;
                return issued;
            }

            if (await PluginNativePrint.PrintAsync(request, title, watermark, IssueAsync, ContinueWhileCurrent, context.Binding.Lifetime, ct).ConfigureAwait(true))
                error = null;
        }).ConfigureAwait(false);
        return error;
    }

    private async Task<bool> ApplyAsync(Window window, IPluginInvocationContext context, Func<Func<bool>, bool> useWhileCurrent,
        Func<Func<Func<bool>, bool>, bool> issue, CancellationToken ct)
    {
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(true)) return false;
        var binding = context.Binding;
        return await packages.TryPostWithContinuationAsync(binding, (grant, continuePost) =>
            !ct.IsCancellationRequested && dispatcher.HasThreadAccess && ReferenceEquals(ActiveWindow.Current, window) &&
            grant.AccountKeys.SetEquals(context.Grant.AccountKeys) && grant.Permissions.SetEquals(context.Grant.Permissions) &&
            grant.ApprovedHttpOrigins.SetEquals(context.Grant.ApprovedHttpOrigins) &&
            useWhileCurrent(() =>
            {
                context.MarkMutationIssued();
                return issue(continuePost);
            }), ct).ConfigureAwait(true);
    }

    private async Task<string?> TitleAsync(IPluginInvocationContext context, CancellationToken ct)
    {
        var package = context.Binding.Package;
        var installation = await packages.FindAsync(package.Id, ct).ConfigureAwait(false);
        if (!installation.IsOk || installation.Value is not { Enabled: true } current || current.Package.Identity != package) return null;
        var manifest = current.Package.Manifest;
        var name = manifest.TryGetProperty("name", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return PluginToastMethods.SourceLabel(name, package.Id);
    }
}
#endif
