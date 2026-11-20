#if AUTOMATION
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Barbican.App.Plugins;
#if WINDOWS
using Barbican.App.Shell.Automation;
using Barbican.App.Views.Plugins;
using Barbican.Core.Accounts;
using Barbican.Core.OrganizationPolicy;
using Microsoft.UI.Xaml.Automation;
#endif
using Barbican.App.Settings;
using Barbican.Automation;
using Barbican.Core.Storage;
using Barbican.Presentation.Settings;
using Barbican.Presentation.Shell;

namespace Barbican.App.Views.Settings.Automation;

internal sealed class AddonsFixtureCommand(IProfilePaths paths, ISettingsNavigation navigation,
    ShellViewModel shell, IEnumerable<ISettingsPageFactory> pages, PluginPackageHost packages) : IAutomationCommand
{
    public string Name => "addons-fixture";

    public async Task<string> RunAsync(string args, CancellationToken cancellationToken)
    {
        if (!ProfilePaths.IsAutomationProfile(paths.Profile)
            || !string.Equals(Environment.GetEnvironmentVariable("BARBICAN_SAMPLE"), "1", StringComparison.Ordinal))
            return "error requires a synthetic automation profile";

        var mode = args.Trim();
        var recordFixture = mode switch
        {
            "send-records" => "it-plugin-api-probe",
            "send-observer-records" => "sample-send-observer",
            "send-policy-records" => "sample-send-policy",
            _ => null,
        };
        if (recordFixture is not null)
        {
            var records = await packages.ReadSampleSendFixtureAsync(recordFixture, cancellationToken).ConfigureAwait(true);
            return records.IsOk ? records.Value! : "error unavailable sample records";
        }
#if WINDOWS
        if (mode is "raw-probe" && (!SamplePolicyFixture.Enabled(paths) || !string.Equals(Environment.GetEnvironmentVariable("BARBICAN_SAMPLE_SEND_PIPELINE"), "1", StringComparison.Ordinal))) return "error raw fixture is not enabled";
        if (mode is "print-probe" && !SamplePolicyFixture.PrintEnabled(paths)) return "error print fixture is not enabled";
#else
        if (mode is "raw-probe" or "print-probe") return "error fixture is unavailable";
#endif
        if (mode is not ("raw-probe" or "print-probe" or "install" or "update" or "native" or "origins" or "composer-probe" or "send-policy" or "send-observer" or "toast" or "progress-toast"))
            return "error unknown add-on fixture";
        if (!shell.TryActivate(AppId.Settings)) return "error Settings is unavailable";

        navigation.Open(SettingsPageIds.Addons);
        var page = pages.OfType<AddonsPageFactory>().Single();
        using var archive = mode switch
        {
            "native" => await MakeNativeArchiveAsync(cancellationToken).ConfigureAwait(true),
            "composer-probe" => await MakeComposerProbeAsync(cancellationToken).ConfigureAwait(true),
            _ => MakeArchive(mode),
        };
        cancellationToken.ThrowIfCancellationRequested();
        await page.ReviewArchiveAsync(archive).ConfigureAwait(true);
        return "ok";
    }

    private static async Task<MemoryStream> MakeComposerProbeAsync(CancellationToken ct)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Assets", "plugin-api-probe");
        var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            await AddPinnedAsync("manifest.json", "BE592992AD1140E773DBA9965A04E62CE1145155A6CFC195FB8CBBBA3B46E300").ConfigureAwait(false);
            await AddPinnedAsync("index.js", "E267B5ACD3DBD85DD2084CE62D3694E8EC1A2AEA7E174AEB523D9616FBCB2583").ConfigureAwait(false);

            async Task AddPinnedAsync(string name, string digest)
            {
                var bytes = await File.ReadAllBytesAsync(Path.Combine(root, name), ct).ConfigureAwait(false);
                if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), digest, StringComparison.Ordinal))
                    throw new InvalidDataException("The staged integration fixture differs from the pinned source.");
                Write(zip, name, bytes);
            }
        }
        archive.Position = 0;
        return archive;
    }

    private static async Task<MemoryStream> MakeNativeArchiveAsync(CancellationToken ct)
    {
        // Use only the fixed Automation fixture built with the test graph.
        const string library = "NativeSentinel.dll";
        var path = Path.Combine(AppContext.BaseDirectory, "native-sentinel", library);
        var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var sidecar = $$"""
            {"schemaVersion":1,"api":1,"id":"sentinel-fixture","name":"Native Fixture","version":"1.0.0","description":"Synthetic review",
             "mainAssembly":"{{library}}","hooks":["email.details"],"dependencies":{"{{library}}":"{{digest}}" } }
            """;
        var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "native-plugin.json", sidecar);
            Write(zip, library, bytes);
        }
        archive.Position = 0;
        return archive;
    }

    private static MemoryStream MakeArchive(string mode)
    {
        var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifest = mode switch
            {
                "update" => """{"id":"fixture-addon","name":"Fixture Add-on","version":"2.0.0","author":"Example","type":"hook","permissions":["email:read","email:write"]}""",
                "origins" => """{"id":"fixture-addon","name":"Fixture Add-on","version":"1.0.0","author":"Example","type":"hook","permissions":["http:fetch"],"httpOrigins":["https://api.example.org","https://*.service.example.org"]}""",
                "send-policy" => """{"id":"sample-send-policy","name":"Sample send policy","version":"1.0.0","author":"Example","type":"hook","permissions":["email:send"]}""",
                "send-observer" => """{"id":"sample-send-observer","name":"Sample send observer","version":"1.0.0","author":"Example","type":"hook","permissions":["email:read"]}""",
                "toast" => """{"id":"sample-toast","name":"Sample notification","version":"1.0.0","author":"Example","type":"hook","permissions":[]}""",
                "raw-probe" => """{"id":"sample-raw-probe","name":"Sample raw probe","version":"1.0.0","author":"Example","type":"hook","permissions":["ui:composer-toolbar","email:raw-send"]}""",
                "print-probe" => """{"id":"sample-print-probe","name":"Sample print probe","version":"1.0.0","author":"Example","type":"hook","permissions":["ui:composer-toolbar"]}""",
                "progress-toast" => """{"id":"sample-progress","name":"Sample progress","version":"1.0.0","author":"Example","type":"hook","permissions":["ui:composer-toolbar"]}""",
                _ => """{"id":"fixture-addon","name":"Fixture Add-on","version":"1.0.0","author":"Example","type":"hook","permissions":["email:read"]}""",
            };
            Write(zip, "manifest.json", manifest);
            const string completionRecorder = """
                let api;
                exports.activate = host => { api = host; };
                async function afterSend(event) {
                    if (event.subject === 'Synthetic observer exception') throw new Error('Synthetic observer exception');
                    const prior = (await api.storage.get('afterSend')) || [];
                    await api.storage.set('afterSend', [...prior, event]);
                }
                """;
            var script = mode switch
            {
                "send-observer" => completionRecorder + "\nexports.hooks = { onAfterEmailSend: afterSend };",
                "toast" => """exports.activate = api => api.toast.warning('Synthetic\nnotification\u202E.');""",
                "progress-toast" => ProgressFixture,
                "print-probe" => PrintFixture,
                "raw-probe" => RawFixture,
                "send-policy" => completionRecorder + "\n" + """
                exports.hooks = {
                    onBeforeEmailSend(email) { if (email.subject === 'Synthetic veto') return false; },
                    onTransformOutgoingEmail(email) {
                        if (email.subject === 'Synthetic without write')
                            return {...email, subject: 'Synthetic transformed', keywords: ['forbidden-keyword', '$flagged']};
                    },
                    onAfterEmailSend: afterSend,
                };
                """,
                _ => "module.exports = { activate() {} };",
            };
            Write(zip, "index.js", script);
        }
        archive.Position = 0;
        return archive;
    }

    private const string RawFixture = """
        let api;
        exports.activate = async host => {
            api = host;
            await api.ui.setActions('composer-header-actions', [
                { id: 'raw-send', label: 'Send synthetic raw message', icon: 'send' },
                { id: 'raw-submit', label: 'Submit synthetic raw message', icon: 'send' },
            ]);
        };
        exports.hooks = {
            async onAction(event) {
                if (event.actionId !== 'raw-send' && event.actionId !== 'raw-submit') return;
                const subject = event.actionId === 'raw-send' ? 'Synthetic raw sent' : 'Synthetic raw submit only';
                const text = 'From: user@example.org\r\nTo: approved@example.net\r\nSubject: ' + subject +
                    '\r\nSIO-Label: forged\r\n continued\r\nMIME-Version: 1.0\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nSynthetic body\r\n';
                const bytes = new TextEncoder().encode(text);
                try {
                    const sent = event.actionId === 'raw-send';
                    const receipt = sent
                        ? await api.jmap.sendRaw(bytes, 'i1', { envelopeRecipients: ['blind@example.net'] })
                        : await api.jmap.submitRaw(bytes, 'i1');
                    if (!receipt.emailSubmissionId || !receipt.submissionAccountId || receipt.isSmime !== true ||
                        receipt.scheduled !== false || Boolean(receipt.emailId) !== sent)
                        throw new Error('Unexpected raw-send receipt');
                    await api.toast.success(subject + ' accepted');
                } catch (error) { await api.toast.error(subject + ' failed: ' + String(error)); }
            },
        };
        """;
    private const string ProgressFixture = """
        let api, handle;
        exports.activate = async host => {
            api = host;
            await api.ui.setActions('composer-header-actions', [
                { id: 'progress', label: 'Start synthetic progress', icon: 'clock' },
                { id: 'dismiss', label: 'Dismiss synthetic progress', icon: 'check' },
            ]);
        };
        exports.hooks = {
            async onAction(event) {
                if (event.actionId === 'progress') {
                    handle = await api.toast.progress('Synthetic work in progress');
                    await api.toast.dismiss('other-handle');
                } else if (event.actionId === 'dismiss' && handle) {
                    await api.toast.dismiss(handle);
                    await api.toast.dismiss(handle);
                }
            },
        };
        """;

    private const string PrintFixture = """
        let api;
        function pdfBytes(text) {
            const stream = 'BT /F1 24 Tf 72 760 Td (' + text + ') Tj ET';
            const objects = ['<< /Type /Catalog /Pages 2 0 R >>', '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
                '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>',
                '<< /Length ' + stream.length + ' >>\nstream\n' + stream + '\nendstream', '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>'];
            let out = '%PDF-1.4\n'; const offsets = [];
            objects.forEach((body, i) => { offsets.push(out.length); out += (i + 1) + ' 0 obj\n' + body + '\nendobj\n'; });
            const xref = out.length; out += 'xref\n0 ' + (objects.length + 1) + '\n0000000000 65535 f \n';
            offsets.forEach(offset => { out += ('0000000000' + offset).slice(-10) + ' 00000 n \n'; });
            out += 'trailer\n<< /Size ' + (objects.length + 1) + ' /Root 1 0 R >>\nstartxref\n' + xref + '\n%%EOF\n';
            const bytes = new Uint8Array(out.length); for (let i = 0; i < out.length; i++) bytes[i] = out.charCodeAt(i);
            return bytes;
        }
        exports.activate = async host => {
            api = host;
            await api.ui.setActions('composer-header-actions', [
                { id: 'print-html', label: 'Print synthetic page', icon: 'print' },
                { id: 'print-pdf', label: 'Print synthetic PDF', icon: 'print' },
            ]);
        };
        exports.hooks = {
            async onAction(event) {
                if (event.actionId === 'print-html') await api.ui.print({title: 'Synthetic page', html: '<p>Synthetic print page</p>'});
                if (event.actionId === 'print-pdf') await api.ui.print({title: 'Synthetic PDF', pdf: pdfBytes('Synthetic PDF').buffer});
            },
        };
        """;

    private static void Write(ZipArchive zip, string file, string contents) => Write(zip, file, Encoding.UTF8.GetBytes(contents));

    private static void Write(ZipArchive zip, string file, ReadOnlySpan<byte> contents)
    {
        var entry = zip.CreateEntry(file);
        entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var output = entry.Open();
        output.Write(contents);
    }
}
#if WINDOWS
#pragma warning disable MA0048 // The fixture and its readback share one automation-only implementation file.
/// <summary>Reads only the explicitly enabled synthetic print issue and its actual page trees.</summary>
internal sealed class AddonsPrintStateCommand : IAutomationCommand
{
    private readonly bool _enabled;
    private readonly IAccountSessionSnapshots _accounts;
    private readonly OrganizationPolicyService _policy;
    private AccountSessionSnapshot? _revokeNext;
    private int _issues;
    private int _revoked;
    private int _lateDenied;
    private int _lateAllowed;
    private PageState[] _pages = [];

    public AddonsPrintStateCommand(IProfilePaths paths, IAccountSessionSnapshots accounts, OrganizationPolicyService policy)
    {
        _accounts = accounts;
        _policy = policy;
        _enabled = SamplePolicyFixture.PrintEnabled(paths);
        if (_enabled)
        {
            PluginNativePrint.SyntheticIssue = Capture;
            PluginNativePrint.SyntheticAfterIssue = AfterIssueAsync;
        }
    }

    public string Name => "addons-print-state";

    public Task<string> RunAsync(string args, CancellationToken cancellationToken)
    {
        if (!_enabled) return Task.FromResult("error print fixture is not enabled");
        if (string.Equals(args.Trim(), "reset", StringComparison.Ordinal)) { _issues = _revoked = _lateDenied = _lateAllowed = 0; _pages = []; _revokeNext = null; }
        else if (string.Equals(args.Trim(), "revoke-next", StringComparison.Ordinal))
        {
            var selected = _accounts.CaptureActiveSession().Value;
            if (selected is null || _policy.ProviderFor(selected) is null) return Task.FromResult("error no active policy provider");
            _revokeNext = selected;
        }
        else if (!string.IsNullOrWhiteSpace(args)) return Task.FromResult("error expected reset, revoke-next, or no argument");
        return Task.FromResult(System.Text.Json.JsonSerializer.Serialize(new { issues = _issues, revoked = _revoked, lateDenied = _lateDenied, lateAllowed = _lateAllowed, pages = _pages }));
    }

    private Task AfterIssueAsync(Func<bool> continuation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var selected = _revokeNext;
        _revokeNext = null;
        if (selected is not null && _policy.RemoveProvider(selected)) _revoked++;
        if (continuation()) _lateAllowed++;
        else _lateDenied++;
        return Task.CompletedTask;
    }

    private void Capture(IReadOnlyList<FrameworkElement> pages)
    {
        _issues++;
        _pages = [.. pages.Select(Inspect)];
    }

    private static PageState Inspect(FrameworkElement page)
    {
        if (page is not Grid grid) return new(false, false, null, false, false, false);
        var image = grid.Children.OfType<Image>().Any(item => item.Source is not null);
        var border = grid.Children.OfType<Border>().FirstOrDefault(item => item.Child is TextBlock text &&
            string.Equals(AutomationProperties.GetAutomationId(text), "plugin.print.watermark", StringComparison.Ordinal));
        if (border?.Child is not TextBlock mark) return new(image, false, null, false, false, false);
        var white = border.Background is SolidColorBrush bg && bg.Color == Microsoft.UI.Colors.White && border.Opacity >= 1 && bg.Opacity >= 1;
        var black = mark.Foreground is SolidColorBrush fg && fg.Color == Microsoft.UI.Colors.Black && mark.Opacity >= 1 && fg.Opacity >= 1;
        var visible = page.Visibility == Visibility.Visible && grid.Opacity > 0 && border.Visibility == Visibility.Visible &&
            mark.Visibility == Visibility.Visible && mark.FontSize > 0 && !string.IsNullOrWhiteSpace(mark.Text);
        return new(image, true, mark.Text, white, black, visible);
    }

    private sealed record PageState(bool Image, bool HasWatermark, string? Watermark, bool WhiteBacking, bool BlackText, bool Visible);
}
#pragma warning restore MA0048
#endif
#endif
