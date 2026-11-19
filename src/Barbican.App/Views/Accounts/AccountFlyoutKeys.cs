using Barbican.Presentation.Input;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Barbican.App.Views.Accounts;

internal sealed class AccountFlyoutKeys : IModalSurface
{
    private FlyoutBase? _open;

    public bool IsOpen => _open is not null;

    public void Track(FlyoutBase flyout)
    {
        flyout.Opened += (_, _) => _open = flyout;
        flyout.Closed += (_, _) =>
        {
            if (ReferenceEquals(_open, flyout)) _open = null;
        };
    }

    public bool HandleKey(KeyChord chord)
    {
        if (_open is null || !string.Equals(chord.Key, "Escape", StringComparison.OrdinalIgnoreCase)) return false;
        _open.Hide();
        return true;
    }
}
