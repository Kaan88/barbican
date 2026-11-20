#if WINDOWS
// Managed add-on enrollment sends the selected login's OAuth credential only after explicit consent.
using System.Security.Cryptography;
using Barbican.App.Shell;
using Barbican.Core.Accounts;
using Barbican.Core.OrganizationPolicy;
using Barbican.Core.Plugins.Packages;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Settings;

internal sealed partial class AddonsPageFactory
{
    private TextBox? _managedOrigin;
    private TextBox? _managedKey;
    private TextBox? _managedId;
    private ManagedSelection? _managedSelection;
    private CancellationTokenSource? _managedFetch;

    private sealed record ManagedSelection(AccountSessionSnapshot Account, Uri Origin, string Key, string PluginId, string Fingerprint);

    private void AddManagedReviewControls(StackPanel panel)
    {
        panel.Children.Add(SetUiUi.Heading("Review managed add-on"));
        panel.Children.Add(SetUiUi.Meta("Use the add-on provider and signing key supplied by your administrator."));
        _managedOrigin = SetUiUi.Id(new TextBox { PlaceholderText = "https://provider.example.org" }, "set.addons.managed.origin");
        _managedKey = SetUiUi.Id(new TextBox { PlaceholderText = "Ed25519 public key (base64)" }, "set.addons.managed.key");
        _managedId = SetUiUi.Id(new TextBox { PlaceholderText = "Add-on ID" }, "set.addons.managed.id");
        AutomationProperties.SetName(_managedOrigin, "Managed add-on provider origin");
        AutomationProperties.SetName(_managedKey, "Managed add-on signing public key");
        AutomationProperties.SetName(_managedId, "Managed add-on ID");
        panel.Children.Add(_managedOrigin);
        panel.Children.Add(_managedKey);
        panel.Children.Add(_managedId);
        panel.Children.Add(SetUiUi.Pill("Review managed add-on", () => SetUiUi.Run(PrepareManagedReviewAsync), "set.addons.managed.prepare"));
    }

    private void ClearManagedSelection()
    {
        CancelManagedFetch();
        _managedSelection = null;
    }

    private void CancelManagedFetch()
    {
        var pending = _managedFetch;
        _managedFetch = null;
        pending?.Cancel();
    }

    private bool ManagedSelectionCurrent() => _managedSelection is { } selected &&
        ViewHost.Get<IAccountSessionSnapshots>().IsSessionCurrent(selected.Account);

    private async Task PrepareManagedReviewAsync()
    {
        if (!_visible || _loading || _importing || _mutating || _reviewPanel is null || _status is null) return;
        var generation = ++_reviewGeneration;
        await ClearReviewAsync().ConfigureAwait(true);
        if (!IsCurrent(generation)) return;
        ClearManagedSelection();
        var selected = ViewHost.Get<IAccountSessionSnapshots>().CaptureActiveSession().Value;
        var key = _managedKey?.Text.Trim() ?? string.Empty;
        if (selected is null || !Uri.TryCreate(_managedOrigin?.Text.Trim(), UriKind.Absolute, out var origin) ||
            !OrganizationPolicyService.IsProviderOrigin(origin) || !TryFingerprint(key, out var fingerprint) ||
            string.IsNullOrWhiteSpace(_managedId?.Text))
        {
            _status.Text = "Select a connected account and enter an HTTPS origin, signing key and add-on ID.";
            return;
        }
        _managedSelection = new(selected, origin, key, _managedId!.Text.Trim(), fingerprint);
        var confirmed = _managedSelection;
        _reviewPanel.Children.Add(SetUiUi.Heading("Share access with this provider"));
        _reviewPanel.Children.Add(SetUiUi.Body("Account: " + AccountLabel(selected.Session.Account)));
        _reviewPanel.Children.Add(SetUiUi.Body("Provider: " + origin.GetLeftPart(UriPartial.Authority)));
        _reviewPanel.Children.Add(SetUiUi.Body("Public key: " + key));
        _reviewPanel.Children.Add(SetUiUi.Meta("SHA-256: " + fingerprint));
        _reviewPanel.Children.Add(SetUiUi.Body("The provider will receive this account's OAuth access token to verify your login and return the add-on."));
        var consent = SetUiUi.Id(new CheckBox { Content = "I trust this provider with the selected account's access token." }, "set.addons.managed.token-consent");
        var next = SetUiUi.Pill("Continue to add-on review", () =>
        {
            if (consent.IsChecked == true && ReferenceEquals(_managedSelection, confirmed) && ManagedSelectionCurrent())
                SetUiUi.Run(() => FetchManagedReviewAsync(confirmed, generation));
        }, "set.addons.managed.fetch", primary: true);
        next.IsEnabled = false;
        consent.Checked += (_, _) => next.IsEnabled = true;
        consent.Unchecked += (_, _) => next.IsEnabled = false;
        _reviewPanel.Children.Add(consent);
        _reviewPanel.Children.Add(next);
        _reviewPanel.Children.Add(SetUiUi.Pill("Cancel", () => SetUiUi.Run(CancelReviewAsync), "set.addons.cancel"));
        _status.Text = "Review the account and provider before continuing.";
    }

    private async Task FetchManagedReviewAsync(ManagedSelection selection, int generation)
    {
        if (!IsCurrent(generation) || !ReferenceEquals(selection, _managedSelection) || !ManagedSelectionCurrent() || _importing || _status is null) return;
        var pending = new CancellationTokenSource();
        _managedFetch = pending;
        _importing = true;
        SetActionsEnabled(false);
        try
        {
            _status.Text = "Checking the managed add-on…";
            var result = await packages.ReviewManagedAsync(selection.Account, selection.Origin, selection.Key, selection.PluginId, pending.Token).ConfigureAwait(true);
            if (pending.IsCancellationRequested || !ReferenceEquals(_managedFetch, pending) || !IsCurrent(generation) ||
                !ManagedSelectionCurrent() || !ReferenceEquals(selection, _managedSelection))
            {
                if (result.IsOk && result.Value is { } abandoned) await packages.DiscardReviewAsync(abandoned.ReviewId, CancellationToken.None).ConfigureAwait(true);
                if (IsCurrent(generation)) _status.Text = "The selected account changed. Review again.";
                return;
            }
            await ApplyReviewAsync(result, generation).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { }
#pragma warning disable CA1031 // Provider failures must not disclose credentials or crash settings.
        catch (Exception) { if (IsCurrent(generation)) _status.Text = "Could not verify the managed add-on."; }
#pragma warning restore CA1031
        finally
        {
            if (ReferenceEquals(_managedFetch, pending)) _managedFetch = null;
            pending.Dispose();
            _importing = false;
            SetActionsEnabled(_review is null && !_loading && !_mutating);
        }
    }

    private void ShowManagedConsent(PluginInstallPreview preview, StackPanel panel)
    {
        var selected = _managedSelection!;
        if (!ManagedSelectionCurrent() || !string.Equals(preview.ManagedAccountKey, selected.Account.Session.Account.Key, StringComparison.Ordinal) ||
            !Equals(preview.ManagedOrigin, selected.Origin) || !string.Equals(preview.VerifiedSigner, selected.Fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            panel.Children.Add(SetUiUi.Body("The managed review no longer matches the selected account and provider."));
            panel.Children.Add(SetUiUi.Pill("Cancel", () => SetUiUi.Run(CancelReviewAsync), "set.addons.cancel"));
            return;
        }
        panel.Children.Add(SetUiUi.Heading("Privileged JavaScript add-on"));
        panel.Children.Add(SetUiUi.Body("This add-on requests privileged access. It will be installed disabled while its runtime is unavailable."));
        panel.Children.Add(SetUiUi.Meta("Account: " + AccountLabel(selected.Account.Session.Account)));
        panel.Children.Add(SetUiUi.Meta("Verified signing key SHA-256: " + preview.VerifiedSigner));
        var consent = SetUiUi.Id(new CheckBox { Content = "I approve this privileged add-on and its requested permissions for this account." }, "set.addons.managed.privileged-consent");
        var account = new CheckBox { IsChecked = true };
        var install = SetUiUi.Pill("Install disabled", () =>
        {
            if (consent.IsChecked == true && ManagedSelectionCurrent())
                SetUiUi.Run(() => InstallAsync(preview, [(selected.Account.Session.Account.Key, account)], approvePrivileged: true));
        }, "set.addons.install", primary: true);
        install.IsEnabled = false;
        consent.Checked += (_, _) => install.IsEnabled = true;
        consent.Unchecked += (_, _) => install.IsEnabled = false;
        panel.Children.Add(consent);
        panel.Children.Add(install);
        panel.Children.Add(SetUiUi.Pill("Cancel", () => SetUiUi.Run(CancelReviewAsync), "set.addons.cancel"));
    }

    private static bool TryFingerprint(string key, out string fingerprint)
    {
        fingerprint = string.Empty;
        try
        {
            var bytes = Convert.FromBase64String(key);
            if (bytes.Length != 32 || !string.Equals(Convert.ToBase64String(bytes), key, StringComparison.Ordinal)) return false;
            fingerprint = Convert.ToHexStringLower(SHA256.HashData(bytes));
            return true;
        }
        catch (FormatException) { return false; }
    }
}
#endif
