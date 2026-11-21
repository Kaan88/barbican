#if AUTOMATION && WINDOWS
// The synthetic add-on behind `addons-fixture dialog-probe`: installs and enables an add-on whose reader actions call ui.alert, ui.confirm and ui.prompt (Bulwark host-api.ts:2236-2290),
// reports each resolved value back as a toast (read with `toast-last`), and drives the dialog on screen without input (the form's own press, field and key methods, in process).
// Synthetic data only: the sample account of a BARBICAN_SAMPLE run, nothing leaves the process.
using System.IO.Compression;
using System.Text;
using Barbican.App.Plugins;
using Barbican.App.Shell;
using Barbican.App.Views.Plugins;
using Barbican.Presentation.Dialogs;
using Windows.System;

namespace Barbican.App.Views.Settings.Automation;

internal static class DialogProbeFixture
{
    internal const string PluginId = "sample-dialog-probe";

    private const string Manifest = """{"id":"sample-dialog-probe","name":"Sample dialog probe","version":"1.0.0","author":"Example","type":"hook","permissions":["ui:toolbar"]}""";

    private const string Script = """
        let api, step = 0;
        const message = 'Synthetic notice for the probe.\nSecond line of the message.';
        const confirmOptions = { title: 'Probe confirm', message: 'Proceed with the synthetic step?', confirmLabel: 'Proceed', cancelLabel: 'Not now' };
        const dangerOptions = { title: 'Delete synthetic item', message: 'This removes the synthetic item.', confirmLabel: 'Delete', danger: true };
        const promptOptions = { title: 'Probe prompt', message: 'Enter the synthetic values.', confirmLabel: 'Send', fields: [
            { name: 'user', label: 'User name', placeholder: 'name@example.org', required: true },
            { name: 'secret', label: 'Secret', type: 'password', required: true },
            { name: 'note', label: 'Note (optional)', placeholder: 'optional' }] };
        const scenarios = [
            ['alert-ok', () => api.ui.alert({ title: 'Probe alert', message })],
            ['alert-escape', () => api.ui.alert({ title: 'Probe alert', message })],
            ['alert-enter', () => api.ui.alert({ title: 'Probe alert', message })],
            ['confirm-ok', () => api.ui.confirm({ message: 'Proceed with the synthetic step?' })],
            ['confirm-cancel', () => api.ui.confirm(confirmOptions)],
            ['confirm-escape', () => api.ui.confirm(confirmOptions)],
            ['confirm-enter', () => api.ui.confirm(confirmOptions)],
            ['danger-enter', () => api.ui.confirm(dangerOptions)],
            ['danger-ok', () => api.ui.confirm(dangerOptions)],
            ['danger-cancel', () => api.ui.confirm(dangerOptions)],
            ['danger-escape', () => api.ui.confirm(dangerOptions)],
            ['prompt-ok', () => api.ui.prompt(promptOptions)],
            ['prompt-cancel', () => api.ui.prompt(promptOptions)],
            ['prompt-enter', () => api.ui.prompt(promptOptions)],
            ['prompt-escape', () => api.ui.prompt(promptOptions)],
            ['concurrent-first', () => api.ui.confirm(confirmOptions)],
            ['disable-open', () => api.ui.confirm(dangerOptions)],
        ];
        async function report(name, work) {
            let text;
            try { text = String(JSON.stringify(await work())); } catch (error) { text = 'ERR ' + String((error && error.message) || error); }
            await api.toast.info('dp|' + name + '|' + text);
        }
        exports.activate = async host => {
            api = host;
            await api.ui.setActions('reader-actions', [
                { id: 'run', label: 'Run next dialog', icon: 'file' },
                { id: 'second', label: 'Try a second dialog', icon: 'file' },
                { id: 'reset', label: 'Restart the sequence', icon: 'file' },
            ]);
        };
        exports.hooks = {
            async onAction(event) {
                if (event.actionId === 'reset') { step = 0; await api.toast.info('dp|reset|0'); }
                else if (event.actionId === 'second') await report('second-dialog', () => api.ui.alert({ message: 'A second dialog.' }));
                else if (event.actionId === 'run' && step < scenarios.length) { const [name, work] = scenarios[step++]; await report(name, work); }
            },
        };
        """;

    internal static async Task<string> RunAsync(string mode, PluginPackageHost packages, CancellationToken ct)
    {
        var parts = mode.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var sub = parts.Length > 1 ? parts[1] : "install";
        var rest = parts.Length > 2 ? parts[2] : string.Empty;
        var form = PluginDialogContent.Current;
        switch (sub)
        {
            case "install":
                return await InstallAsync(packages, ct).ConfigureAwait(true);
            case "disable":
            {
                var item = await packages.FindAsync(PluginId, ct).ConfigureAwait(true);
                if (!item.IsOk || item.Value is null) return "error not installed";
                var result = await ViewHost.Get<PluginBackgroundRuntimes>().SetEnabledAsync(item.Value.Package.Identity, false, ct).ConfigureAwait(true);
                return result.IsOk ? "ok" : "error " + result.Error?.Message;
            }
            case "status":
            {
                var item = await packages.FindAsync(PluginId, ct).ConfigureAwait(true);
                if (!item.IsOk || item.Value is null) return "none";
                return $"installed enabled={item.Value.Enabled} running={ViewHost.Get<PluginBackgroundRuntimes>().IsRunning(item.Value)}";
            }
            case "state":
                return ViewHost.Get<DialogService>().IsOpen && form is not null ? "open " + form.Describe() : "none";
            case "press":
                return form is not null && form.Press(rest) ? "ok" : "refused";
            case "field":
            {
                var pair = rest.Split(' ', 2, StringSplitOptions.None);
                return form is not null && int.TryParse(pair[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index) &&
                    form.SetField(index, pair.Length > 1 ? pair[1] : string.Empty) ? "ok" : "refused";
            }
            case "key":
                return form is null ? "none" : form.Key(rest.StartsWith("Enter", StringComparison.Ordinal) ? VirtualKey.Enter : rest.StartsWith("Escape", StringComparison.Ordinal) ? VirtualKey.Escape : VirtualKey.None,
                    rest.EndsWith("nonbutton", StringComparison.Ordinal));
            default:
                return "error unknown dialog-probe step";
        }
    }

    private static async Task<string> InstallAsync(PluginPackageHost packages, CancellationToken ct)
    {
        var runtimes = ViewHost.Get<PluginBackgroundRuntimes>();
        await runtimes.WaitUntilInitializedAsync(ct).ConfigureAwait(true);
        var existing = await packages.FindAsync(PluginId, ct).ConfigureAwait(true);
        var identity = existing.IsOk ? existing.Value?.Package.Identity : null;
        if (identity is null)
        {
            using var archive = MakeArchive();
            var review = await packages.InspectAsync(archive, ct).ConfigureAwait(true);
            if (!review.IsOk || review.Value is not { } preview) return "error " + review.Error?.Message;
            try
            {
                var accounts = packages.EligibleAccounts.Select(account => account.Key).ToArray();
                if (accounts.Length == 0) return "error no sample account";
                var installed = await packages.InstallAsync(new Barbican.Core.Plugins.Packages.PluginInstallDecision(preview.ReviewId, preview.Candidate.Identity,
                    preview.Candidate.RequestedPermissions, accounts), ct).ConfigureAwait(true);
                if (!installed.IsOk) return "error " + installed.Error?.Message;
                identity = preview.Candidate.Identity;
            }
            finally
            {
                await packages.DiscardReviewAsync(preview.ReviewId, CancellationToken.None).ConfigureAwait(true);
            }
        }

        var enabled = await runtimes.SetEnabledAsync(identity, true, ct).ConfigureAwait(true);
        return enabled.IsOk ? "ok" : "error " + enabled.Error?.Message;
    }

    private static MemoryStream MakeArchive()
    {
        var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "manifest.json", Manifest);
            Write(zip, "index.js", Script);
        }

        archive.Position = 0;
        return archive;
    }

    private static void Write(ZipArchive zip, string file, string contents)
    {
        var entry = zip.CreateEntry(file);
        entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var output = entry.Open();
        output.Write(Encoding.UTF8.GetBytes(contents));
    }
}
#endif
