#if WINDOWS
using Barbican.App.Plugins;
using Barbican.App.Shell;
using Barbican.Core.Plugins.Bridge;
using Barbican.Presentation.Common;
using Barbican.Presentation.Dialogs;

namespace Barbican.App.Views.Plugins;

internal sealed class PluginFilePresenter(IDispatcher dispatcher, IDialogService dialogs, PluginPackageHost packages)
{
    internal async Task<PluginErrorCode?> ShowAsync(PluginPreviewFile file, IPluginInvocationContext context, CancellationToken ct)
    {
        var error = PluginErrorCode.Unavailable;
        var shown = false;
        await dispatcher.InvokeAsync(async () =>
        {
            if (dialogs.IsOpen || ActiveWindow.Current is null) return;
            PluginPdfPreview? pdf = null;
            try
            {
                UIElement? preview;
                if (string.Equals(file.ContentType, "application/pdf", StringComparison.Ordinal))
                {
                    pdf = new PluginPdfPreview();
                    preview = await pdf.LoadAsync(file.Bytes, ct).ConfigureAwait(true) ? pdf : null;
                }
                else
                {
                    preview = await PluginRasterPreview.LoadAsync(file.Bytes, ct).ConfigureAwait(true);
                }
                if (preview is null) { error = PluginErrorCode.InvalidArguments; return; }
                if (!await context.CheckCurrentAsync(ct).ConfigureAwait(true))
                {
                    error = PluginErrorCode.StaleContext;
                    return;
                }
                var binding = context.Binding;
                Task<bool>? dialog = null;
                shown = await packages.TryPostAsync(binding, grant =>
                {
                    if (ct.IsCancellationRequested || dialogs.IsOpen || ActiveWindow.Current is null ||
                        !grant.AccountKeys.SetEquals(context.Grant.AccountKeys) ||
                        !grant.Permissions.SetEquals(context.Grant.Permissions)) return false;
                    return binding.TryWriteWhileAlive(() =>
                    {
                        context.MarkMutationIssued();
                        dialog = dialogs.ShowAsync(new PluginFilePreviewRequest(file.Name, preview), binding.Lifetime);
                    });
                }, ct).ConfigureAwait(true);
                if (!shown || dialog is null) { error = PluginErrorCode.StaleContext; return; }
                if (dialog.IsCompleted)
                {
                    _ = dialog.Exception;
                    shown = false;
                    return;
                }
                _ = ReleasePreviewAsync(dialog, pdf);
                pdf = null;
            }
            finally
            {
                pdf?.Dispose();
            }
        }).ConfigureAwait(false);
        return shown ? null : error;
    }

    private static async Task ReleasePreviewAsync(Task<bool> dialog, PluginPdfPreview? pdf)
    {
#pragma warning disable VSTHRD003 // The dialog was started on the same UI dispatcher and is only awaited asynchronously.
        try { _ = await dialog.ConfigureAwait(true); }
#pragma warning restore VSTHRD003
        finally { pdf?.Dispose(); }
    }
}
#endif
