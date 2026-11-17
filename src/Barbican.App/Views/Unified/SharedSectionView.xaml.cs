// Mirrors: Bulwark components/layout/sidebar.tsx:2133-2566 (the shared accounts section). spec I-054. Owner unified (m2-close-a). Collapsed while there is nothing to show.

using System.ComponentModel;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Unified;

namespace Barbican.App.Views.Unified;

/// <summary>The shared accounts section of the sidebar.</summary>
public sealed partial class SharedSectionView : UserControl
{
    public SharedSectionView()
    {
        Model = ViewHost.Get<SharedSectionViewModel>();
        Look = ViewHost.Get<StyleState>();
        InitializeComponent();
        Apply();
        Loaded += (_, _) =>
        {
            Model.PropertyChanged -= OnChanged;
            Model.PropertyChanged += OnChanged;
            Apply();
        };
        Unloaded += (_, _) => Model.PropertyChanged -= OnChanged;
    }

    public SharedSectionViewModel Model { get; }

    public StyleState Look { get; }

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => Apply();

    private void Apply() => Section.Visibility = Model.IsVisible ? Visibility.Visible : Visibility.Collapsed;
}
