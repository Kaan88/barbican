// Mirrors: M2 plan section 3.3 (slots). Stage F of M2.

using Microsoft.Extensions.DependencyInjection;

namespace Barbican.App.Shell;

/// <summary>Fills a slot (a ContentPresenter) from the registered slot content; an empty slot collapses so it costs nothing.</summary>
public static class SlotHost
{
    /// <summary>Sets the presenter's content to the last registered filler of the named slot, or collapses the presenter when there is none.</summary>
    public static void Fill(ContentPresenter presenter, string slot)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        var filler = ViewHost.Services.GetServices<ISlotContent>().LastOrDefault(s => string.Equals(s.Slot, slot, StringComparison.Ordinal));
        if (filler is null)
        {
            presenter.Visibility = Visibility.Collapsed;
            return;
        }

        presenter.Content = filler.Create();
    }
}
