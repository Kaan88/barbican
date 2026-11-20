// Installed package metadata only; enabled status does not assert that a runtime is running.
using Barbican.App.Settings;
using Barbican.Core.Accounts;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
#if !AUTOMATION
using Barbican.App.Shell;
using Windows.Storage.Pickers;
#endif
using Barbican.App.Plugins;
using Barbican.Presentation.Settings;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;

namespace Barbican.App.Views.Settings;

/// <summary>Lists packages and reviews local archives before installation.</summary>
internal sealed class AddonsPageFactory(PluginPackageHost packages) : ISettingsPageFactory
{
    private StackPanel? _list;
    private TextBlock? _status;
    private Button? _refresh;
    private UIElement? _page;
    private bool _visible;
    private bool _loading;
    private bool _importing;
    private StackPanel? _reviewPanel;
    private PluginInstallPreview? _review;
    private Button? _import;
    private int _reviewGeneration;

    public string PageId => SettingsPageIds.Addons;

    public UIElement Create()
    {
        if (_page is not null)
        {
            return _page;
        }

        var panel = SetUiUi.Page("Installed add-ons");
        _refresh = SetUiUi.Pill("Refresh", () => SetUiUi.Run(LoadAsync), "set.addons.refresh");
        panel.Children.Add(_refresh);
        _import = SetUiUi.Pill("Import ZIP…", () => SetUiUi.Run(ImportAsync), "set.addons.import");
        panel.Children.Add(_import);
        _reviewPanel = SetUiUi.Id(new StackPanel { Spacing = 6 }, "set.addons.review");
        panel.Children.Add(_reviewPanel);
        panel.Unloaded += (_, _) => OnHidden();
        _status = SetUiUi.Id(SetUiUi.Meta("Loading installed add-ons…"), "set.addons.status");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        panel.Children.Add(_status);
        _list = SetUiUi.Id(new StackPanel { Spacing = 4 }, "set.addons.list");
        panel.Children.Add(_list);
        _page = SetUiUi.Scroll(panel);
        return _page;
    }

    public void OnShown()
    {
        _ = Create();
        if (_visible) return;
        _visible = true;
        SetUiUi.Run(LoadAsync);
    }

    public void OnHidden()
    {
        _visible = false;
        _reviewGeneration++;
        SetUiUi.Run(ClearReviewAsync);
    }

    private bool IsCurrent(int generation) => _visible && generation == _reviewGeneration;

    public void Reveal(string anchor)
    {
        // There are no individual searchable settings on this page.
    }

    private async Task LoadAsync()
    {
        if (_loading || _status is null || _list is null || _refresh is null)
        {
            return;
        }

        _loading = true;
        _refresh.IsEnabled = false;
        _list.Children.Clear();
        _status.Text = "Loading installed add-ons…";
        try
        {
            var result = await packages.ListAsync(CancellationToken.None).ConfigureAwait(true);
            if (!result.IsOk || result.Value is null)
            {
                _status.Text = "Could not load installed add-ons. Try refreshing.";
                return;
            }

            if (result.Value.Count == 0)
            {
                _status.Text = "No add-ons are installed.";
                return;
            }

            foreach (var installation in result.Value)
            {
                var identity = installation.Package.Identity;
                var state = installation.Enabled ? "Enabled in settings" : "Disabled in settings";
                var detail = $"Version {identity.Version} · {state}";
                if (!string.IsNullOrWhiteSpace(installation.Error))
                {
                    detail += " · Needs attention";
                }

                _list.Children.Add(SetUiUi.RowOf(identity.Id, detail, null, "set.addons.item." + identity.Id));
            }

            _status.Text = $"Installed add-ons: {result.Value.Count}";
        }
#pragma warning disable CA1031 // Loading failures must be visible on the page, including unexpected store failures.
        catch (Exception)
#pragma warning restore CA1031
        {
            _status.Text = "Could not load installed add-ons. Try refreshing.";
        }
        finally
        {
            _loading = false;
            _refresh.IsEnabled = true;
        }
    }
    private async Task ImportAsync()
    {
        if (!_visible || _importing || _import is null || _status is null) return;
        var generation = ++_reviewGeneration;
        _importing = true;
        _import.IsEnabled = false;
        try
        {
            await ClearReviewAsync().ConfigureAwait(true);
            if (!IsCurrent(generation)) return;
#if AUTOMATION
            _status.Text = "ZIP import is unavailable in automation.";
#else
            if (ActiveWindow.Current is not { } window)
            {
                _status.Text = "Could not open the file picker.";
                return;
            }
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Downloads };
            picker.FileTypeFilter.Add(".zip");
#if WINDOWS
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
#endif
            var file = await picker.PickSingleFileAsync();
            if (!IsCurrent(generation)) return;
            if (file is null)
            {
                _status.Text = "Import canceled.";
                return;
            }
            await using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            await InspectArchiveAsync(stream, generation).ConfigureAwait(true);
#endif
        }
#pragma warning disable CA1031 // Picker and package failures must be shown without crashing settings.
        catch (Exception)
#pragma warning restore CA1031
        {
            if (IsCurrent(generation)) _status.Text = "Could not import the add-on. Try again.";
        }
        finally
        {
            _importing = false;
            _import.IsEnabled = true;
        }
    }

#if AUTOMATION
    internal async Task ReviewArchiveAsync(Stream archive)
    {
        if (!_visible || _importing) return;
        var generation = ++_reviewGeneration;
        await ClearReviewAsync().ConfigureAwait(true);
        if (IsCurrent(generation)) await InspectArchiveAsync(archive, generation).ConfigureAwait(true);
    }
#endif

    private async Task InspectArchiveAsync(Stream archive, int generation)
    {
        var result = await packages.InspectAsync(archive, CancellationToken.None).ConfigureAwait(true);
        if (!IsCurrent(generation))
        {
            if (result.IsOk && result.Value is { } abandoned)
                await packages.DiscardReviewAsync(abandoned.ReviewId, CancellationToken.None).ConfigureAwait(true);
            return;
        }
        if (!result.IsOk || result.Value is null)
        {
            _status!.Text = result.Error?.Message ?? "Could not inspect the add-on.";
            return;
        }
        _review = result.Value;
        ShowReview(result.Value);
        _status!.Text = "Review the add-on before installing.";
    }

    private void ShowReview(PluginInstallPreview preview)
    {
        if (_reviewPanel is null) return;
        _reviewPanel.Children.Clear();
        var candidate = preview.Candidate;
        _reviewPanel.Children.Add(SetUiUi.Heading("Review add-on"));
        _reviewPanel.Children.Add(SetUiUi.Body($"{candidate.Identity.Id} · Version {candidate.Identity.Version}"));
        if (preview.Previous is { } previous)
        {
            _reviewPanel.Children.Add(SetUiUi.Body($"Update from version {previous.Package.Identity.Version}"));
            var added = candidate.RequestedPermissions.Except(previous.Package.RequestedPermissions, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal);
            var removed = previous.Package.RequestedPermissions.Except(candidate.RequestedPermissions, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal);
            _reviewPanel.Children.Add(SetUiUi.Meta("Added permissions: " + Describe(added)));
            _reviewPanel.Children.Add(SetUiUi.Meta("Removed permissions: " + Describe(removed)));
        }
        _reviewPanel.Children.Add(SetUiUi.Heading("Requested permissions"));
        _reviewPanel.Children.Add(SetUiUi.Body(Describe(candidate.RequestedPermissions.OrderBy(x => x, StringComparer.Ordinal))));
        if (candidate.RequestedTier != PluginExecutionTier.Untrusted)
        {
            _reviewPanel.Children.Add(SetUiUi.Body("Privileged add-ons require verified managed approval."));
            _reviewPanel.Children.Add(SetUiUi.Pill("Cancel", () => SetUiUi.Run(CancelReviewAsync), "set.addons.cancel"));
            return;
        }
        ShowConsent(preview, _reviewPanel);
    }

    private void ShowConsent(PluginInstallPreview preview, StackPanel panel)
    {
        panel.Children.Add(SetUiUi.Heading("Allow access to these accounts"));
        var choices = new List<(string Key, CheckBox Choice)>();
        foreach (var account in packages.EligibleAccounts)
        {
            var choice = SetUiUi.Id(new CheckBox { Content = AccountLabel(account), IsChecked = false }, "set.addons.account." + account.Key);
            choices.Add((account.Key, choice));
            panel.Children.Add(choice);
        }
        if (choices.Count == 0) panel.Children.Add(SetUiUi.Meta("No connected mail accounts are available."));
        var consent = SetUiUi.Id(new CheckBox
        {
            Content = "I reviewed and approve the requested permissions and selected accounts.",
        }, "set.addons.consent");
        var scope = SetUiUi.Id(SetUiUi.Meta(string.Empty), "set.addons.scope");
        if (preview.Previous is not null) panel.Children.Add(scope);
        AddConsentActions(preview, panel, choices, consent, scope);
    }

    private void AddConsentActions(PluginInstallPreview preview, StackPanel panel, List<(string Key, CheckBox Choice)> choices, CheckBox consent, TextBlock scope)
    {
        var install = SetUiUi.Pill("Install disabled", () =>
        {
            if (consent.IsChecked == true) SetUiUi.Run(() => InstallAsync(preview, choices));
        }, "set.addons.install", primary: true);
        install.IsEnabled = false;
        consent.Checked += (_, _) => install.IsEnabled = true;
        consent.Unchecked += (_, _) => install.IsEnabled = false;
        foreach (var (_, choice) in choices)
        {
            choice.Checked += (_, _) => SelectionChanged();
            choice.Unchecked += (_, _) => SelectionChanged();
        }
        SelectionChanged();
        panel.Children.Add(consent);
        panel.Children.Add(install);
        panel.Children.Add(SetUiUi.Pill("Cancel", () => SetUiUi.Run(CancelReviewAsync), "set.addons.cancel"));

        void SelectionChanged()
        {
            consent.IsChecked = false;
            if (preview.Previous is not null) SetAccountDiff(preview.Previous, choices, scope);
        }
    }

    private void SetAccountDiff(PluginInstallation previous, List<(string Key, CheckBox Choice)> choices, TextBlock scope)
    {
        var earlier = previous.Grant?.AccountKeys ?? System.Collections.Immutable.ImmutableHashSet<string>.Empty;
        var selected = choices.Where(choice => choice.Choice.IsChecked == true).Select(choice => choice.Key).ToHashSet(StringComparer.Ordinal);
        var labels = packages.AccountStatuses.ToDictionary(status => status.Account.Key, status => AccountLabel(status.Account), StringComparer.Ordinal);
        string Format(IEnumerable<string> keys) =>
            Describe(keys.Select(key => labels.TryGetValue(key, out var label) ? label : FormerAccountLabel(key)).OrderBy(label => label, StringComparer.Ordinal));
        scope.Text = $"Previously allowed accounts - {Format(earlier)}\nAdded accounts - {Format(selected.Except(earlier, StringComparer.Ordinal))}\nRemoved accounts - {Format(earlier.Where(key => !selected.Contains(key)))}";
    }

    private static string AccountLabel(AccountInfo account) => AccountLabel(account.User, account.Server);

    private static string FormerAccountLabel(string key)
    {
        var separator = key.IndexOf('\n', StringComparison.Ordinal);
        if (separator <= 0 || separator == key.Length - 1) return "Unavailable account";
        var user = key[(separator + 1)..];
        return user.Any(char.IsControl) ? "Unavailable account" : AccountLabel(user, key[..separator]);
    }

    private static string AccountLabel(string user, string endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var server) && server.IsAbsoluteUri && server.Host.Length > 0
            ? $"{user} ({server.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.SafeUnescaped)})"
            : user;

    private async Task InstallAsync(PluginInstallPreview preview, List<(string Key, CheckBox Choice)> choices)
    {
        if (!_visible || _importing || !ReferenceEquals(_review, preview) || _status is null) return;
        var generation = _reviewGeneration;
        _importing = true;
        _review = null;
        _reviewPanel?.Children.Clear();
        if (_import is not null) _import.IsEnabled = false;
        try
        {
            var decision = new PluginInstallDecision(preview.ReviewId, preview.Candidate.Identity,
                preview.Candidate.RequestedPermissions, choices.Where(x => x.Choice.IsChecked == true).Select(x => x.Key));
            var result = await packages.InstallAsync(decision, CancellationToken.None).ConfigureAwait(true);
            if (!IsCurrent(generation)) return;
            if (!result.IsOk || result.Value is null)
            {
                _status.Text = "Could not install the add-on: " + result.Error?.Message;
                return;
            }
            await LoadAsync().ConfigureAwait(true);
            if (IsCurrent(generation)) _status.Text = "Add-on installed and disabled.";
        }
#pragma warning disable CA1031 // Installation failures must be visible and release the staged review.
        catch (Exception)
#pragma warning restore CA1031
        {
            if (IsCurrent(generation)) _status.Text = "Could not install the add-on. Try again.";
        }
        finally
        {
            await packages.DiscardReviewAsync(preview.ReviewId, CancellationToken.None).ConfigureAwait(true);
            _importing = false;
            if (_import is not null) _import.IsEnabled = true;
        }
    }

    private async Task CancelReviewAsync()
    {
        var generation = ++_reviewGeneration;
        await ClearReviewAsync().ConfigureAwait(true);
        if (IsCurrent(generation)) _status!.Text = "Import canceled.";
    }

    private async Task ClearReviewAsync()
    {
        var review = _review;
        _review = null;
        _reviewPanel?.Children.Clear();
        if (review is not null)
            await packages.DiscardReviewAsync(review.ReviewId, CancellationToken.None).ConfigureAwait(true);
    }

    private static string Describe(IEnumerable<string> values)
    {
        var text = string.Join(", ", values);
        return text.Length == 0 ? "None" : text;
    }

}
