// Mirrors: M6 plan section 4.1 (ISettingsPageFactory: one per page id, the screen builds the page once from it). Owner srv-ui.

using Barbican.App.Settings;

namespace Barbican.App.Views.SettingsServer;

/// <summary>The factory of one server-backed settings page.</summary>
internal sealed class SrvUiPageFactory(string pageId, Func<SrvUiPage> create) : ISettingsPageFactory
{
    private SrvUiPage? _page;

    public string PageId => pageId;

    public UIElement Create()
    {
        if (_page is null)
        {
            _page = create();
            _page.Start();
        }

        return _page;
    }

    public void OnShown() => _page?.Shown();

    public void Reveal(string anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        _page?.Reveal(anchor);
    }
}
