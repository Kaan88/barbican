// Installed package management with a separate live background-runtime status.
using Barbican.App.Settings;
using Barbican.App.Shell;
using Barbican.Presentation.Dialogs;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
#if !AUTOMATION
using Windows.Storage.Pickers;
#endif
using Barbican.App.Plugins;
using Barbican.Presentation.Settings;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;

namespace Barbican.App.Views.Settings;

/// <summary>Lists packages and reviews local archives before installation.</summary>
internal sealed class AddonsPageFactory(PluginPackageHost packages, PluginBackgroundRuntimes runtimes) : ISettingsPageFactory
{
    private StackPanel? _list;
    private TextBlock? _status;
    private Button? _refresh;
    private UIElement? _page;
    private bool _visible;
    private bool _loading;
    private bool _importing;
    private bool _mutating;
    private readonly List<Button> _actions = [];
    private readonly List<(PluginInstallation Installation, TextBlock Detail, Button? Start)> _rows = [];
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
        runtimes.RunningChanged += OnRunningChanged;
        SetUiUi.Run(LoadAsync);
    }

    public void OnHidden()
    {
        if (_visible) runtimes.RunningChanged -= OnRunningChanged;
        _visible = false;
        _reviewGeneration++;
        SetUiUi.Run(ClearReviewAsync);
    }

    private bool IsCurrent(int generation) => _visible && generation == _reviewGeneration;

    private void OnRunningChanged(object? sender, EventArgs args)
    {
        _ = _list?.DispatcherQueue.TryEnqueue(() =>
        {
            if (!_visible) return;
            foreach (var (installation, detail, start) in _rows) UpdateRunningState(installation, detail, start);
        });
    }

    private void UpdateRunningState(PluginInstallation installation, TextBlock detail, Button? start)
    {
        var running = runtimes.IsRunning(installation);
        var state = installation.Enabled ? running ? "Running" : "Enabled · Stopped" : "Disabled";
        if (installation.Package.RuntimeKind == PluginRuntimeKind.Native) state += " · Native";
        if (installation.Error is not null) state += " · Needs attention";
        if (NeedsWebsiteReview(installation)) state += " · Review websites";
        detail.Text = $"Version {installation.Package.Identity.Version} · {state}";
        if (start is not null) start.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetActionsEnabled(bool enabled)
    {
        foreach (var action in _actions) action.IsEnabled = enabled;
    }

    public void Reveal(string anchor)
    {
        // There are no individual searchable settings on this page.
    }

    private async Task LoadAsync()
    {
        if (!_visible || _loading || _mutating || _status is null || _list is null || _refresh is null)
        {
            return;
        }

        var generation = _reviewGeneration;
        _loading = true;
        _refresh.IsEnabled = false;
        if (_import is not null) _import.IsEnabled = false;
        _actions.Clear();
        _rows.Clear();
        _list.Children.Clear();
        _status.Text = "Loading installed add-ons…";
        try
        {
            var result = await packages.ListAsync(CancellationToken.None).ConfigureAwait(true);
            if (!IsCurrent(generation)) return;
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

            foreach (var installation in result.Value) AddInstallationRow(_list, installation);

            _status.Text = $"Installed add-ons: {result.Value.Count}";
        }
#pragma warning disable CA1031 // Loading failures must be visible on the page, including unexpected store failures.
        catch (Exception)
#pragma warning restore CA1031
        {
            if (IsCurrent(generation)) _status.Text = "Could not load installed add-ons. Try refreshing.";
        }
        finally
        {
            _loading = false;
            _refresh.IsEnabled = !_mutating;
            if (_import is not null) _import.IsEnabled = !_importing && !_mutating;
            SetActionsEnabled(!_mutating && !_importing && _review is null);
            if (_visible && _review is null && !IsCurrent(generation)) SetUiUi.Run(LoadAsync);
        }
    }

    private void AddInstallationRow(StackPanel list, PluginInstallation installation)
    {
        var identity = installation.Package.Identity;
        var detail = SetUiUi.Id(SetUiUi.Meta(string.Empty), "set.addons.state." + identity.Id);
        AutomationProperties.SetLiveSetting(detail, AutomationLiveSetting.Polite);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var start = installation is { Enabled: true, Error: null }
            ? AddAction("Start", "start", enabled: true, remove: false)
            : null;
        AddAction(installation.Enabled ? "Disable" : "Enable", "enable", enabled: !installation.Enabled, remove: false);
        AddAction("Remove", "remove", enabled: false, remove: true);
        if (NeedsWebsiteReview(installation))
        {
            var review = SetUiUi.Pill("Review websites", () => SetUiUi.Run(() => ReviewInstalledAsync(identity)), $"set.addons.review-websites.{identity.Id}");
            actions.Children.Add(review);
            _actions.Add(review);
        }
        _rows.Add((installation, detail, start));
        UpdateRunningState(installation, detail, start);
        list.Children.Add(SetUiUi.RowWithDescription(identity.Id, detail, actions, "set.addons.item." + identity.Id));

        Button AddAction(string label, string action, bool enabled, bool remove)
        {
            var button = SetUiUi.Pill(label, () => SetUiUi.Run(() => ChangeAsync(identity, enabled, remove)), $"set.addons.{action}.{identity.Id}");
            AutomationProperties.SetName(button, label + " " + identity.Id);
            actions.Children.Add(button);
            _actions.Add(button);
            return button;
        }
    }

    private static bool NeedsWebsiteReview(PluginInstallation item) =>
        item.Error is null && !item.Package.RequestedHttpOrigins.IsEmpty && item.Grant is { ApprovedHttpOrigins.IsEmpty: true };

    private async Task ReviewInstalledAsync(PluginPackageIdentity identity)
    {
        if (!_visible || _importing || _mutating || _loading || _review is not null || _status is null || _import is null) return;
        var generation = ++_reviewGeneration;
        _importing = true;
        _import.IsEnabled = false;
        SetActionsEnabled(false);
        try
        {
            var result = await packages.ReviewAsync(identity, CancellationToken.None).ConfigureAwait(true);
            await ApplyReviewAsync(result, generation).ConfigureAwait(true);
        }
#pragma warning disable CA1031 // Report package review failures without exposing internal paths from Settings.
        catch (Exception) { if (IsCurrent(generation)) _status.Text = "Could not review the add-on. Try again."; }
#pragma warning restore CA1031
        finally
        {
            _importing = false;
            _import.IsEnabled = true;
            SetActionsEnabled(_review is null);
        }
    }

    private async Task ChangeAsync(PluginPackageIdentity identity, bool enabled, bool remove)
    {
        if (!_visible || _loading || _importing || _mutating || _review is not null || _status is null) return;
        _mutating = true;
        var generation = _reviewGeneration;
        if (_refresh is not null) _refresh.IsEnabled = false;
        if (_import is not null) _import.IsEnabled = false;
        SetActionsEnabled(false);
        string? message = null;
        try
        {
            if (remove)
            {
                var dialogs = ViewHost.Find<IDialogService>();
                if (dialogs is null || !await dialogs.ShowAsync(new ConfirmDialogRequest("Remove add-on", "Remove " + identity.Id + "?", "Remove", true), CancellationToken.None).ConfigureAwait(true) || !IsCurrent(generation)) return;
            }
            var result = remove
                ? await runtimes.UninstallAsync(identity, CancellationToken.None).ConfigureAwait(true)
                : await runtimes.SetEnabledAsync(identity, enabled, CancellationToken.None).ConfigureAwait(true);
            message = result.IsOk ? "Add-on settings updated." : "Could not complete the change. Check its current state below.";
        }
#pragma warning disable CA1031 // The operation may have partially completed; reload without exposing internal errors.
        catch (Exception)
#pragma warning restore CA1031
        {
            message = "Could not complete the change. Check its current state below.";
        }
        finally
        {
            _mutating = false;
            if (_refresh is not null) _refresh.IsEnabled = true;
            if (_import is not null) _import.IsEnabled = !_importing;
            SetActionsEnabled(true);
            if (_visible) await LoadAsync().ConfigureAwait(true);
            if (message is not null && IsCurrent(generation)) _status.Text = message;
        }
    }

    private async Task ImportAsync()
    {
        if (!_visible || _importing || _mutating || _loading || _import is null || _status is null) return;
        var generation = ++_reviewGeneration;
        _importing = true;
        _import.IsEnabled = false;
        SetActionsEnabled(false);
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
            SetActionsEnabled(_review is null);
        }
    }

#if AUTOMATION
    internal async Task ReviewArchiveAsync(Stream archive)
    {
        if (!_visible || _importing || _mutating) return;
        var generation = ++_reviewGeneration;
        await ClearReviewAsync().ConfigureAwait(true);
        if (IsCurrent(generation)) await InspectArchiveAsync(archive, generation).ConfigureAwait(true);
    }
#endif

    private async Task InspectArchiveAsync(Stream archive, int generation)
    {
        var result = await packages.InspectAsync(archive, CancellationToken.None).ConfigureAwait(true);
        await ApplyReviewAsync(result, generation).ConfigureAwait(true);
    }

    private async Task ApplyReviewAsync(Result<PluginInstallPreview> result, int generation)
    {
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
        SetActionsEnabled(false);
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
        if (candidate.RuntimeKind == PluginRuntimeKind.Native)
        {
            ShowNativeConsent(preview, _reviewPanel);
            return;
        }
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
        if (!candidate.RequestedHttpOrigins.IsEmpty || preview.Previous?.Grant is { ApprovedHttpOrigins.IsEmpty: false })
        {
            _reviewPanel.Children.Add(SetUiUi.Heading("Requested websites"));
            _reviewPanel.Children.Add(SetUiUi.Id(SetUiUi.Body(Describe(candidate.RequestedHttpOrigins.Order(StringComparer.OrdinalIgnoreCase))),
                "set.addons.http-origins"));
            if (preview.Previous?.Grant is { } grant)
                _reviewPanel.Children.Add(SetUiUi.Meta("Previously approved websites: " + Describe(grant.ApprovedHttpOrigins.Order(StringComparer.OrdinalIgnoreCase))));
        }
        if (candidate.RequestedTier != PluginExecutionTier.Untrusted)
        {
            _reviewPanel.Children.Add(SetUiUi.Body("Privileged add-ons require verified managed approval."));
            _reviewPanel.Children.Add(SetUiUi.Pill("Cancel", () => SetUiUi.Run(CancelReviewAsync), "set.addons.cancel"));
            return;
        }
        ShowConsent(preview, _reviewPanel);
    }

    private void ShowNativeConsent(PluginInstallPreview preview, StackPanel panel)
    {
        if (preview.Previous is { } previous)
            panel.Children.Add(SetUiUi.Body($"Update from version {previous.Package.Identity.Version}"));
        panel.Children.Add(SetUiUi.Heading("Native add-on with full access"));
        panel.Children.Add(SetUiUi.Body("Native add-ons can access the same files and accounts as Barbican. After installation, choose Enable to run this add-on."));
        var hooks = preview.Candidate.Manifest.GetProperty("hooks").EnumerateArray().Select(hook => hook.GetString()!);
        panel.Children.Add(SetUiUi.Meta("Declared hooks: " + Describe(hooks)));
        var consent = SetUiUi.Id(new CheckBox { Content = "I trust this native add-on." }, "set.addons.native.consent");
        var install = SetUiUi.Pill("Install disabled", () =>
        {
            if (consent.IsChecked == true) SetUiUi.Run(() => InstallAsync(preview, [], approveNative: true));
        }, "set.addons.install", primary: true);
        install.IsEnabled = false;
        consent.Checked += (_, _) => install.IsEnabled = true;
        consent.Unchecked += (_, _) => install.IsEnabled = false;
        panel.Children.Add(consent);
        panel.Children.Add(install);
        panel.Children.Add(SetUiUi.Pill("Cancel", () => SetUiUi.Run(CancelReviewAsync), "set.addons.cancel"));
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
            Content = preview.Candidate.RequestedHttpOrigins.IsEmpty
                ? "I reviewed and approve the requested permissions and selected accounts."
                : "I approve the requested permissions, websites and selected accounts.",
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

    private async Task InstallAsync(PluginInstallPreview preview, List<(string Key, CheckBox Choice)> choices, bool approveNative = false)
    {
        if (!_visible || _importing || _mutating || _loading || !ReferenceEquals(_review, preview) || _status is null) return;
        var generation = _reviewGeneration;
        _importing = true;
        _review = null;
        _reviewPanel?.Children.Clear();
        if (_import is not null) _import.IsEnabled = false;
        try
        {
            var decision = new PluginInstallDecision(preview.ReviewId, preview.Candidate.Identity,
                preview.Candidate.RequestedPermissions, choices.Where(x => x.Choice.IsChecked == true).Select(x => x.Key),
                approvedHttpOrigins: preview.Candidate.RequestedHttpOrigins, approveNativeFullTrust: approveNative);
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
            SetActionsEnabled(!_loading && !_mutating && _review is null);
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
        SetActionsEnabled(!_importing && !_loading && !_mutating);
        if (review is not null)
            await packages.DiscardReviewAsync(review.ReviewId, CancellationToken.None).ConfigureAwait(true);
    }

    private static string Describe(IEnumerable<string> values)
    {
        var text = string.Join(", ", values);
        return text.Length == 0 ? "None" : text;
    }

}
