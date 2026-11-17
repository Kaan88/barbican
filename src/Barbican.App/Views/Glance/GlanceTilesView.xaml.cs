using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Shell;

namespace Barbican.App.Views.Glance;

/// <summary>The summary tiles (Bento); the data is <see cref="GlanceTilesViewModel"/>.</summary>
public sealed partial class GlanceTilesView : UserControl
{
    public GlanceTilesView()
    {
        Glance = ViewHost.Get<GlanceTilesViewModel>();
        Look = ViewHost.Get<StyleState>();
        InitializeComponent();
    }

    public GlanceTilesViewModel Glance { get; }

    public StyleState Look { get; }
}
