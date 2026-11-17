// Mirrors: M1-plan §4.3 (Folders, Tags, sections, Storage); spec I-038..I-063. Contract stub from F2; owner mailcore.

using System.Collections.ObjectModel;
using Barbican.Presentation.Mail;

namespace Barbican.Presentation.Shell;

/// <summary>The mail sidebar.</summary>
public sealed class SidebarViewModel
{
    public ObservableCollection<FolderRowViewModel> Folders { get; } = [];
}
