// Mirrors: Rust src/rte/model.rs (tag rust-final); Bulwark components/email/email-composer.tsx (TipTap schema).
// No RFC or Stalwart counterpart: the composer document model is client-side only.

namespace Barbican.Core.Editor;

public static class MarkExtensions
{
    public static bool IsOn(this Mark mark, Marks marks)
    {
        ArgumentNullException.ThrowIfNull(marks);
        return mark switch
        {
            Mark.Bold => marks.Bold,
            Mark.Italic => marks.Italic,
            Mark.Underline => marks.Underline,
            Mark.Strike => marks.Strike,
            Mark.Code => marks.Code,
            _ => throw new ArgumentOutOfRangeException(nameof(mark), mark, message: null),
        };
    }

    public static Marks With(this Mark mark, Marks marks, bool on)
    {
        ArgumentNullException.ThrowIfNull(marks);
        return mark switch
        {
            Mark.Bold => marks with { Bold = on },
            Mark.Italic => marks with { Italic = on },
            Mark.Underline => marks with { Underline = on },
            Mark.Strike => marks with { Strike = on },
            Mark.Code => marks with { Code = on },
            _ => throw new ArgumentOutOfRangeException(nameof(mark), mark, message: null),
        };
    }
}
