#if AUTOMATION
// Mirrors: M3 plan 6.3 step 4 (editor-latency: keystroke to painted frame; synthetic IME events). Automation build only.

namespace Barbican.App.Views.Editor;

public sealed partial class EditorView
{
    internal void ScrollTo(double y) => _ = _scroll.ChangeView(null, y, null, disableAnimation: true);

    /// <summary>Completes after the next composed frame (the canvas has painted by then).</summary>
    internal Task NextFrameAsync()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnRendering(object? sender, object e)
        {
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnRendering;
            tcs.TrySetResult();
        }

        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnRendering;
        return tcs.Task;
    }

    /// <summary>The IME composition sequence without an IME: start, update TEXT, end TEXT.</summary>
    internal string SyntheticComposition(string phase, string text)
    {
        switch (phase)
        {
            case "start":
                _composing = true;
                return "ok";
            case "update":
                _composing = true;
                ShowPreedit(text);
                return "ok";
            case "end":
                _ime.Text = text;
                CommitComposition();
                return "ok";
            default:
                return "error usage: editor-ime start | update TEXT | end TEXT";
        }
    }
}
#endif
