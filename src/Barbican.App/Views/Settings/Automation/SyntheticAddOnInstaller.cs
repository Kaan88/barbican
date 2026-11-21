#if AUTOMATION && WINDOWS
// Automation-only (a BARBICAN_SAMPLE run): installs and enables a synthetic add-on the way the Settings review does, for the hidden walkthrough fixtures (dialog-probe, frames-probe). Synthetic data only.
using Barbican.App.Plugins;
using Barbican.App.Shell;
using Barbican.Core.Plugins.Packages;

namespace Barbican.App.Views.Settings.Automation;

internal static class SyntheticAddOnInstaller
{
    /// <summary>Installs the archive made by <paramref name="make"/> (unless <paramref name="id"/> is installed already) for every sample account, then enables it. Returns "ok" or "error ...".</summary>
    internal static async Task<string> InstallAsync(PluginPackageHost packages, string id, Func<MemoryStream> make, CancellationToken ct)
    {
        var runtimes = ViewHost.Get<PluginBackgroundRuntimes>();
        await runtimes.WaitUntilInitializedAsync(ct).ConfigureAwait(true);
        var existing = await packages.FindAsync(id, ct).ConfigureAwait(true);
        var identity = existing.IsOk ? existing.Value?.Package.Identity : null;
        if (identity is null)
        {
            using var archive = make();
            var review = await packages.InspectAsync(archive, ct).ConfigureAwait(true);
            if (!review.IsOk || review.Value is not { } preview) return "error " + review.Error?.Message;
            try
            {
                var accounts = packages.EligibleAccounts.Select(account => account.Key).ToArray();
                if (accounts.Length == 0) return "error no sample account";
                var installed = await packages.InstallAsync(new PluginInstallDecision(preview.ReviewId, preview.Candidate.Identity,
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
}
#endif
