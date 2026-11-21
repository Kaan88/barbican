#if AUTOMATION && WINDOWS
// Automation-only (synthetic profile with BARBICAN_SAMPLE=1): puts two sample add-on pages in the rail without an add-on, so the hidden run can look at the entries, the active state and the page surface (SH-10).
// "pages-sample set" registers the pages of a made-up add-on, "pages-sample open <page> [path]" shows one the way ui.openPage does, "pages-sample clear" ends the made-up runtime (its entries disappear), "pages-sample state" reports.
using System.Globalization;
using System.Text.Json;
using Barbican.Automation;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Pages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Runtime;
using Barbican.Core.Storage;
using Barbican.Presentation.Common;
using Barbican.Presentation.Shell;

namespace Barbican.App.Shell.Automation;

internal sealed class PluginPagesCommand(IProfilePaths paths, PluginPageRegistry pages, PluginAddOnNames names, ShellViewModel shell, IDispatcher dispatcher) : IAutomationCommand
{
    private const string Id = "sample-pages";
    private PluginRuntimeBinding? _binding;

    public string Name => "pages-sample";

    public async Task<string> RunAsync(string args, CancellationToken cancellationToken)
    {
        if (!ProfilePaths.IsAutomationProfile(paths.Profile) || !string.Equals(Environment.GetEnvironmentVariable("BARBICAN_SAMPLE"), "1", StringComparison.Ordinal))
            return "error requires a synthetic automation profile";
        var parts = args.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        switch (parts.FirstOrDefault())
        {
            case "set":
                {
                    using var manifest = JsonDocument.Parse("""{"name":"Sample add-on"}""");
                    names.Remember(Id, manifest.RootElement);
                    var grant = new PluginGrantSnapshot(new PluginPackageIdentity(Id, "1.0.0", new string('c', 64)), 1, true, ["ui:app-page"], []);
                    _binding = PluginRuntimeBinding.Create(paths.Profile, grant, PluginFrameKind.Background);
                    return pages.Set(_binding, [new PluginPage("overview", "Overview", "Star"), new PluginPage("reports", "Reports", null)]) ? "ok" : "error refused";
                }

            case "open" when parts.Length >= 2:
                {
                    var found = pages.Find(Id, parts[1]);
                    if (found is null) return "error no such page";
                    await dispatcher.InvokeAsync(() =>
                    {
                        shell.OpenPluginPage(found.Key, parts.Length > 2 ? parts[2].TrimStart('/') : string.Empty);
                        return Task.CompletedTask;
                    }).ConfigureAwait(false);
                    return "ok";
                }

            case "clear":
                if (_binding is not null) await _binding.DisposeAsync().ConfigureAwait(false);
                _binding = null;
                return "ok";
            case "state":
                return string.Create(CultureInfo.InvariantCulture, $"pages={string.Join(',', pages.All.Select(p => p.Key))} active={shell.ActivePluginPage ?? "-"} path={shell.PluginPagePath}");
            default:
                return "error usage: pages-sample set|open <page> [path]|clear|state";
        }
    }
}
#endif
