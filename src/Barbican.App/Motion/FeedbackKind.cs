// Mirrors: design/MOTION.md "Feedback"; the durations are the generated motion tokens (Tokens.Motion.ColorMs / ButtonMs / MenuMs / FastMs).

namespace Barbican.App.Motion;

/// <summary>Which of the shared colour-transition durations a control's feedback surface uses (Styles/Feedback.xaml sets it with <see cref="FeedbackTransition.KindProperty"/>).</summary>
public enum FeedbackKind
{
    /// <summary>No transition: the state colour is there on the first frame (list rows).</summary>
    None,

    /// <summary>Bulwark <c>transition-colors</c>, 150 ms.</summary>
    Color,

    /// <summary>Bulwark buttons, <c>transition-all duration-200</c>.</summary>
    Button,

    /// <summary>Bulwark menu rows, <c>transition-colors duration-150</c>.</summary>
    Menu,

    /// <summary>A hover action over a row, <c>duration-100</c>.</summary>
    Fast,
}