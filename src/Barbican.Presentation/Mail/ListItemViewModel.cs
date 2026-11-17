// Mirrors: M1-plan §4.3 (Rows: MessageRowViewModel | GroupHeaderViewModel); Rust mail.rs build_rows :463 (a header is a row's first line there). Owner mailcore.

namespace Barbican.Presentation.Mail;

/// <summary>A row of the message list: a message or a date-group header (closed hierarchy).</summary>
public abstract class ListItemViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private protected ListItemViewModel()
    {
    }

    /// <summary>What ListSync matches rows by: the message id, or the header's own key.</summary>
    public abstract string Key { get; }

    /// <summary>Whether another row shows exactly the same, so a refresh leaves the row alone.</summary>
    public abstract bool SameAs(ListItemViewModel other);
}
