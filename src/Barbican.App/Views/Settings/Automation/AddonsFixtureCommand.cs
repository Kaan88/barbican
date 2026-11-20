#if AUTOMATION
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Barbican.App.Plugins;
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
        if (string.Equals(mode, "send-records", StringComparison.Ordinal))
        {
            var records = await packages.ReadSampleSendFixtureAsync(cancellationToken).ConfigureAwait(true);
            return records.IsOk ? records.Value! : "error unavailable sample records";
        }
        if (mode is not ("install" or "update" or "native" or "origins" or "composer-probe" or "send-policy")) return "error unknown add-on fixture";
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
                _ => """{"id":"fixture-addon","name":"Fixture Add-on","version":"1.0.0","author":"Example","type":"hook","permissions":["email:read"]}""",
            };
            Write(zip, "manifest.json", manifest);
            var script = string.Equals(mode, "send-policy", StringComparison.Ordinal) ? """
                exports.hooks = {
                    onBeforeEmailSend(email) { if (email.subject === 'Synthetic veto') return false; },
                    onTransformOutgoingEmail(email) {
                        if (email.subject === 'Synthetic without write')
                            return {...email, subject: 'Synthetic transformed', keywords: ['forbidden-keyword', '$flagged']};
                    },
                };
                """
                : "module.exports = { activate() {} };";
            Write(zip, "index.js", script);
        }
        archive.Position = 0;
        return archive;
    }

    private static void Write(ZipArchive zip, string file, string contents) => Write(zip, file, Encoding.UTF8.GetBytes(contents));

    private static void Write(ZipArchive zip, string file, ReadOnlySpan<byte> contents)
    {
        var entry = zip.CreateEntry(file);
        entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var output = entry.Open();
        output.Write(contents);
    }
}
#endif
