// Bulwark components/email/email-composer.tsx:5121-5131, composer window controls.
#if WINDOWS
using Barbican.App.Composition;
using Microsoft.UI.Windowing;

namespace Barbican.App.Shell;

public sealed partial class ShellPage
{
#if AUTOMATION
    internal string InspectComposerWindow(string action)
    {
        if (action is "sample")
            return ViewHost.Find<Barbican.Presentation.Mail.IActiveMail>() is Automation.SampleActiveMail ? "sample" : "live";
        switch (action)
        {
            case "pop" when _composerWindow is null:
            case "return" when _composerWindow is not null:
                ToggleComposerWindow();
                break;
            case "close" when _composerWindow is not null:
                _ = CloseDetachedComposerAsync();
                break;
        }

        if (_composerWindow is not { } child) return "main";
        var visible = ComposerRegion.IsLoaded && ComposerRegion.XamlRoot == child.Content.XamlRoot;
        if (!visible) return "child loading";
        return ComposerRegion.IsConfirmationVisible ? "child confirmation" : "child loaded";
    }
#endif
    private Window? _composerWindow;
    private bool _windowClosePending;

    private void ToggleComposerWindow()
    {
        if (!ComposerRegion.CanChangeWindow || _windowClosePending || !ReferenceEquals(_composer?.Current, ComposerRegion.Vm)) return;
        if (_composerWindow is not null)
        {
            ReturnComposer();
            ApplyComposer();
            return;
        }

        var options = ViewHost.Get<HiddenWindowOptions>();
        var window = new Window { Title = "Compose message" };
        _composerWindow = window;
        _composerMaximized = false;
        try
        {
            ComposerDockHost.Children.Remove(ComposerRegion);
            ComposerRegion.RequestedTheme = Root.ActualTheme;
            Root.ActualThemeChanged += OnComposerThemeChanged;
            window.Content = ComposerRegion;
            ComposerRegion.SetDetachedWindow(window);
            window.AppWindow.Closing += OnComposerWindowClosing;
            ViewHost.Get<WindowCloseController>().BeforeClose = CloseDetachedComposerAsync;
            if (options.IsHidden)
                HiddenWindow.Show(window, HiddenWindowOptions.HiddenWidth, HiddenWindowOptions.HiddenHeight);
            else
                window.AppWindow.Show(false);
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            ReturnComposer();
            ViewHost.Find<Barbican.Presentation.Actions.IToastService>()?.Show(
                new Barbican.Presentation.Actions.ToastRequest("Could not open the composer window.", IsError: true));
        }

        ApplyComposer();
    }

    private void OnComposerWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        args.Cancel = true;
        _ = CloseDetachedComposerAsync();
    }

    private async Task<bool> CloseDetachedComposerAsync()
    {
        if (_composerWindow is null) return true;
        if (_windowClosePending || !ComposerRegion.CanChangeWindow || _composer is null) return false;
        _windowClosePending = true;
        try
        {
            if (!await _composer.TryCloseAsync(CancellationToken.None)) return false;
            ReturnComposer();
            ApplyComposer();
            return true;
        }
        finally
        {
            _windowClosePending = false;
        }
    }

    private void OnComposerThemeChanged(FrameworkElement sender, object args) =>
        ComposerRegion.RequestedTheme = Root.ActualTheme;

    private void ReturnComposer()
    {
        if (_composerWindow is not { } window) return;
        _composerWindow = null;
        ViewHost.Get<WindowCloseController>().BeforeClose = null;
        window.AppWindow.Closing -= OnComposerWindowClosing;
        Root.ActualThemeChanged -= OnComposerThemeChanged;
        window.Content = null;
        ComposerRegion.RequestedTheme = ElementTheme.Default;
        if (!ComposerDockHost.Children.Contains(ComposerRegion)) ComposerDockHost.Children.Add(ComposerRegion);
        ComposerRegion.SetDetachedWindow(null);
        window.Close();
    }
}
#endif
