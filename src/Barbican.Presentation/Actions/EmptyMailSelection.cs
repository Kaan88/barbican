// Mirrors: M2 plan section 3.3 (replaceable defaults). Stage F of M2; actions registers the real selection.

using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Actions;

/// <summary>The default: nothing is selected.</summary>
public sealed class EmptyMailSelection : IMailSelection
{
    /// <inheritdoc />
    public IReadOnlyList<MailTarget> Current => [];

    /// <inheritdoc />
    public event EventHandler? Changed
    {
        add { }
        remove { }
    }
}
