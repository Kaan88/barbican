#if AUTOMATION && WINDOWS
// The synthetic add-on behind `addons-fixture frames-probe`: installs and enables an add-on that offers the `app-page` and `plugin-dialog` slots (Bulwark plugin-slot-catalog.ts:53,64), sets one rail page (ui.setPages), sets day
// labels for today and a free day a week on (ui.setDayLabels) and calls ui.openDialog from a reader action (host-api.ts:2287). The page and the dialog's slot are React components of the add-on, drawn in its sandboxed frames; the dialog's slot
// answers through its onResult prop. Results are reported as toasts (read with `toast-last`). Synthetic data only: the sample account of a BARBICAN_SAMPLE run, nothing leaves the process.
using System.Globalization;
using System.IO.Compression;
using System.Text;
using Barbican.App.Plugins;
using Barbican.App.Shell;
using Barbican.App.Views.Plugins;
using Barbican.Core.Plugins.Calendar;
using Barbican.Core.Plugins.Pages;
using Barbican.Presentation.Shell;

namespace Barbican.App.Views.Settings.Automation;

internal static class FramesProbeFixture
{
    internal const string PluginId = "sample-frames-probe";

    private const string Manifest = """{"id":"sample-frames-probe","name":"Sample frames probe","version":"1.0.0","author":"Example","type":"hook","permissions":["ui:app-page","ui:calendar-action","ui:observe","ui:toolbar"]}""";

    // An add-on that was never granted ui:observe, the permission of the plugin-dialog slot: its ui.openDialog is refused before anything is drawn.
    internal const string DeniedId = "sample-frames-denied";

    private const string DeniedManifest = """{"id":"sample-frames-denied","name":"Sample frames denied","version":"1.0.0","author":"Example","type":"hook","permissions":["ui:toolbar"]}""";

    private const string DeniedScript = """
        let api;
        exports.activate = async host => {
            api = host;
            await api.ui.setActions('reader-actions', [{ id: 'dialog', label: 'Open dialog without the permission', icon: 'file' }]);
        };
        exports.hooks = {
            async onAction(event) {
                if (event.actionId !== 'dialog') return;
                let text;
                try { text = String(JSON.stringify(await api.ui.openDialog({ slot: 'plugin-dialog' }))); } catch (error) { text = 'ERR ' + String((error && error.message) || error); }
                await api.toast.info('fp|denied|' + text);
            },
        };
        """;

    private const string Script = """
        let api;
        const h = React.createElement;
        function Page(props) {
            return h('div', { id: 'probe-page', style: { padding: 24, fontFamily: 'sans-serif' } },
                h('h1', { style: { margin: 0 } }, 'Frames probe page'),
                h('p', { id: 'probe-page-props' }, 'pageId=' + props.pageId + ' path=' + (props.path || '-')),
                h('p', null, 'This page is drawn by the synthetic add-on in its own frame.'));
        }
        function Dialog(props) {
            React.useEffect(() => {
                if (!props.autoPickMs) return undefined;
                const timer = setTimeout(() => { const button = document.getElementById('pick'); if (button) button.click(); }, props.autoPickMs);
                return () => clearTimeout(timer);
            }, []);
            return h('div', { id: 'probe-dialog', style: { fontFamily: 'sans-serif', padding: 4 } },
                h('p', { id: 'probe-dialog-props' }, 'folder=' + (props.folder || '-') + ' onResult=' + typeof props.onResult),
                h('button', { id: 'pick', onClick: () => { Promise.resolve(props.onResult({ picked: '/docs/a.txt', n: 2 })).then(r => api && api.toast.info('fp|onResult-ack|' + JSON.stringify(r))); } }, 'Pick a.txt'));
        }
        exports.slots = { 'app-page': { component: Page, fill: true }, 'plugin-dialog': { component: Dialog } };
        function day(offset) {
            const d = new Date(); d.setDate(d.getDate() + offset);
            return d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0');
        }
        async function report(name, work) {
            let text;
            try { text = String(JSON.stringify(await work())); } catch (error) { text = 'ERR ' + String((error && error.message) || error); }
            await api.toast.info('fp|' + name + '|' + text);
        }
        exports.activate = async host => {
            api = host;
            await api.ui.setPages([{ id: 'overview', title: 'Frames probe', icon: 'Star' }]);
            await api.ui.setDayLabels([{ date: day(0), text: 'Probe day', title: 'Synthetic holiday' }, { date: day(7), dot: true, title: 'Synthetic dot' }]);
            await api.ui.setActions('reader-actions', [
                { id: 'dialog', label: 'Open probe dialog', icon: 'file' },
                { id: 'dialog-auto', label: 'Open probe dialog, answers itself', icon: 'file' },
                { id: 'dialog-bad', label: 'Open dialog of an unsupported slot', icon: 'file' },
            ]);
        };
        exports.hooks = {
            async onAction(event) {
                if (event.actionId === 'dialog') await report('dialog', () => api.ui.openDialog({ title: 'Probe dialog', slot: 'plugin-dialog', extraProps: { folder: '/docs' }, width: 480 }));
                else if (event.actionId === 'dialog-auto') await report('dialog-auto', () => api.ui.openDialog({ title: 'Probe dialog', slot: 'plugin-dialog', extraProps: { folder: '/docs', autoPickMs: 4000 }, width: 480 }));
                else if (event.actionId === 'dialog-bad') await report('dialog-bad', () => api.ui.openDialog({ slot: 'message-row-actions' }));
            },
        };
        """;

    internal static async Task<string> RunAsync(string mode, PluginPackageHost packages, CancellationToken ct)
    {
        var parts = mode.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var sub = parts.Length > 1 ? parts[1] : "install";
        var rest = parts.Length > 2 ? parts[2] : string.Empty;
        switch (sub)
        {
            case "install":
                return await SyntheticAddOnInstaller.InstallAsync(packages, PluginId, () => MakeArchive(Manifest, Script), ct).ConfigureAwait(true);
            case "install-denied":
                return await SyntheticAddOnInstaller.InstallAsync(packages, DeniedId, () => MakeArchive(DeniedManifest, DeniedScript), ct).ConfigureAwait(true);
            case "status":
            {
                var item = await packages.FindAsync(PluginId, ct).ConfigureAwait(true);
                if (!item.IsOk || item.Value is null) return "none";
                var runtimes = ViewHost.Get<PluginBackgroundRuntimes>();
                var offers = runtimes.ActivationOf(PluginId)?.Slots.Select(s => s.Name + (s.Fill ? "(fill)" : string.Empty)) ?? [];
                return $"installed enabled={item.Value.Enabled} running={runtimes.IsRunning(item.Value)} slots={string.Join('+', offers)}";
            }
            case "labels":
            {
                var all = ViewHost.Get<PluginDayLabelRegistry>().All;
                return string.Create(CultureInfo.InvariantCulture, $"labels={all.Count} {string.Join(' ', all.Select(l => l.Label.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ":" + (l.Label.Text ?? "dot")))}");
            }
            case "open-page":
            {
                var found = ViewHost.Get<PluginPageRegistry>().Find(PluginId, "overview");
                if (found is null) return "error no such page";
                ViewHost.Get<ShellViewModel>().OpenPluginPage(found.Key, rest.TrimStart('/'));
                return "ok " + found.Key;
            }
            case "open-calendar":
                return ViewHost.Get<ShellViewModel>().TryActivate(AppId.Calendar) ? "ok" : "refused";
            case "dialog-state":
                return PluginSlotDialogContent.Current is { } form ? "open " + form.Describe() : "none";
            case "dialog-close":
                return PluginSlotDialogContent.Current is { } open && open.PressClose() ? "ok" : "refused";
            case "disable":
            {
                var item = await packages.FindAsync(PluginId, ct).ConfigureAwait(true);
                if (!item.IsOk || item.Value is null) return "error not installed";
                var result = await ViewHost.Get<PluginBackgroundRuntimes>().SetEnabledAsync(item.Value.Package.Identity, false, ct).ConfigureAwait(true);
                return result.IsOk ? "ok" : "error " + result.Error?.Message;
            }
            case "open-mail":
                return ViewHost.Get<ShellViewModel>().TryActivate(AppId.Mail) ? "ok" : "refused";
            default:
                return "error unknown frames-probe step";
        }
    }

    private static MemoryStream MakeArchive(string manifest, string script)
    {
        var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "manifest.json", manifest);
            Write(zip, "index.js", script);
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
