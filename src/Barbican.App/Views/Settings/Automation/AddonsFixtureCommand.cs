#if AUTOMATION
using System.IO.Compression;
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
        if (mode is not ("install" or "update")) return "error use install or update";
        if (!shell.TryActivate(AppId.Settings)) return "error Settings is unavailable";

        navigation.Open(SettingsPageIds.Addons);
        var page = pages.OfType<AddonsPageFactory>().Single();
        using var archive = MakeArchive(string.Equals(mode, "update", StringComparison.Ordinal));
        cancellationToken.ThrowIfCancellationRequested();
        await page.ReviewArchiveAsync(archive).ConfigureAwait(true);
        return "ok";
    }

    private static MemoryStream MakeArchive(bool update)
    {
        var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifest = update
                ? """{"id":"fixture-addon","name":"Fixture Add-on","version":"2.0.0","author":"Example","type":"hook","permissions":["email:read","email:write"]}"""
                : """{"id":"fixture-addon","name":"Fixture Add-on","version":"1.0.0","author":"Example","type":"hook","permissions":["email:read"]}""";
            Write(zip, "manifest.json", manifest);
            Write(zip, "index.js", "export default {}");
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
