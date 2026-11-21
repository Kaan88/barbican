// Mirrors: design/MOTION.md "Building blocks": Scrim is the dimmed backdrop of a dialog; its colour fades in over Motion.enter, closing removes it at once.
// Black at 50 % (Bulwark dialog.tsx:181 bg-black/50).
// M1 has no dialog yet (the first ones are M2's); the account popover is light-dismiss. The behaviour is here so the dialogs of later milestones use one.

namespace Barbican.App.Motion;

/// <summary>A full-window dimming layer that fades in over Motion.enter and goes away instantly. Place it in the shell's overlay grid, under the dialog card.</summary>
public sealed partial class Scrim : Grid
{
    public Scrim()
    {
        Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(global::Windows.UI.Color.FromArgb(0x80, 0, 0, 0));
        Visibility = Visibility.Collapsed;
        IsHitTestVisible = true;
    }

    /// <summary>Shows the scrim; the fade never delays what is on top of it.</summary>
    public void Show()
    {
        Visibility = Visibility.Visible;
        MotionRuntime.FadeIn(this);
    }

    /// <summary>Removes the scrim at once (the exit is instant).</summary>
    public void Hide() => Visibility = Visibility.Collapsed;
}
