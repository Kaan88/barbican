#if AUTOMATION
// Mirrors: M3 plan 5 (S3: the EditorView hosted in a test page until composer-ui embeds it; the page is automation-only and never shipped), ADR 0007 (in-window overlay).

using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Editor;

namespace Barbican.App.Views.Editor.Automation;

/// <summary>A full-window page with one editor, shown by <c>editor-open</c>.</summary>
public sealed class EditorTestHost : Grid
{
    private EditorView? _view;

    public EditorTestHost()
    {
        Background = AppResources.Brush("Ground");
        Visibility = Visibility.Collapsed;
        Padding = new Thickness(24);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(this, "editor.testhost");
    }

    public static EditorTestHost Instance { get; } = new();

    public static IShellOverlay Overlay { get; } = new ViewOverlay(() => Instance, 30);

    public EditorView? View => _view;

    public EditorView Open(IEditorViewModel vm)
    {
        Children.Clear();
        _view = new EditorView(vm);
        var card = new Border
        {
            Background = AppResources.Brush("Tile"),
            BorderBrush = AppResources.Brush("Line"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            MaxWidth = 760,
            Child = _view,
        };
        Children.Add(card);
        Visibility = Visibility.Visible;
        _ = DispatcherQueue.TryEnqueue(() => _view.FocusEditor());
        return _view;
    }

    public void Close()
    {
        Children.Clear();
        _view = null;
        Visibility = Visibility.Collapsed;
    }
}
#endif
