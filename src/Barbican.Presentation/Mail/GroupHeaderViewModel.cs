// Mirrors: Rust main.rs date grouping (Today, Yesterday, This week, ...); spec I-119. Contract stub from F2; owner mailcore.

namespace Barbican.Presentation.Mail;

/// <summary>A date-group header row.</summary>
public sealed class GroupHeaderViewModel : ListItemViewModel
{
    public GroupHeaderViewModel(string title) => Title = title;

    public string Title { get; }
}
