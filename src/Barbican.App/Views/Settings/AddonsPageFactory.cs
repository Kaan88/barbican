// Installed package metadata only; enabled status does not assert that a runtime is running.
using Barbican.App.Settings;
using Barbican.App.Plugins;
using Barbican.Presentation.Settings;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;

namespace Barbican.App.Views.Settings;

/// <summary>Read-only view of the packages installed in this profile.</summary>
internal sealed class AddonsPageFactory(PluginPackageHost packages) : ISettingsPageFactory
{
    private StackPanel? _list;
    private TextBlock? _status;
    private Button? _refresh;
    private UIElement? _page;
    private bool _loading;

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
        SetUiUi.Run(LoadAsync);
    }

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
}
