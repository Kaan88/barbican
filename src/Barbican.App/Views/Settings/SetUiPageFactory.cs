// Mirrors: M6 plan section 4.1 (ISettingsPageFactory). One class for the pages set-ui builds itself: it holds the three delegates of a page (build once, reload when shown, reveal an anchor).

using Barbican.App.Settings;

namespace Barbican.App.Views.Settings;

/// <summary>A settings page of set-ui.</summary>
public sealed class SetUiPageFactory(string pageId, Func<SetUiPageFactory, UIElement> build, Action? onShown = null, Func<string, bool>? reveal = null) : ISettingsPageFactory
{
    private UIElement? _page;

    public string PageId => pageId;

    /// <summary>The generic rows of the page, when it has any (Reveal goes there).</summary>
    public SetUiRowsView? Rows { get; set; }

    public UIElement Create() => _page ??= build(this);

    public void OnShown() => onShown?.Invoke();

    public void Reveal(string anchor)
    {
        if (reveal is not null)
        {
            _ = reveal(anchor);
        }
        else
        {
            _ = Rows?.Reveal(anchor);
        }
    }
}
