#if AUTOMATION
// Mirrors: spec/automation-protocol.md (M2 actions commands): a row is named by its message id or by #N, the 0-based index among the loaded messages.

using Barbican.Presentation.Mail;

namespace Barbican.App.Views.Actions.Automation;

/// <summary>Finds the row a command argument names.</summary>
internal static class RowRef
{
    public static MessageRowViewModel? Find(IReadOnlyList<MessageRowViewModel> rows, string reference)
    {
        if (reference.StartsWith('#') && int.TryParse(reference.AsSpan(1), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n))
        {
            return n >= 0 && n < rows.Count ? rows[n] : null;
        }

        return rows.FirstOrDefault(r => string.Equals(r.Id, reference, StringComparison.Ordinal));
    }
}
#endif
