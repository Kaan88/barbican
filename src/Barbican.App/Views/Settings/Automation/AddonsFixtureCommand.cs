#if AUTOMATION
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Barbican.App.Settings;
using Barbican.Automation;
using Barbican.Core.Storage;
using Barbican.Presentation.Settings;
using Barbican.Presentation.Shell;

namespace Barbican.App.Views.Settings.Automation;

internal sealed class AddonsFixtureCommand(IProfilePaths paths, ISettingsNavigation navigation,
    ShellViewModel shell, IEnumerable<ISettingsPageFactory> pages) : IAutomationCommand
{
    public string Name => "addons-fixture";

    public async Task<string> RunAsync(string args, CancellationToken cancellationToken)
    {
        if (!ProfilePaths.IsAutomationProfile(paths.Profile)
            || !string.Equals(Environment.GetEnvironmentVariable("BARBICAN_SAMPLE"), "1", StringComparison.Ordinal))
            return "error requires a synthetic automation profile";

        var mode = args.Trim();
        if (mode is not ("install" or "update" or "native" or "origins")) return "error use install, update, native or origins";
        if (!shell.TryActivate(AppId.Settings)) return "error Settings is unavailable";

        navigation.Open(SettingsPageIds.Addons);
        var page = pages.OfType<AddonsPageFactory>().Single();
        using var archive = string.Equals(mode, "native", StringComparison.Ordinal)
            ? await MakeNativeArchiveAsync(cancellationToken).ConfigureAwait(true)
            : MakeArchive(mode);
        cancellationToken.ThrowIfCancellationRequested();
        await page.ReviewArchiveAsync(archive).ConfigureAwait(true);
        return "ok";
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
                _ => """{"id":"fixture-addon","name":"Fixture Add-on","version":"1.0.0","author":"Example","type":"hook","permissions":["email:read"]}""",
            };
            Write(zip, "manifest.json", manifest);
            Write(zip, "index.js", "module.exports = { activate() {} };");
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
