using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Mail;

namespace Barbican.App.Views.Reader;

/// <summary>The reader chrome. The data is <see cref="ReaderViewModel"/>; the details panel is the only state of its own (it folds again when another message opens).</summary>
public sealed partial class ReaderView : UserControl
{
    private readonly MessageListViewModel _list = ViewHost.Get<MessageListViewModel>();
    private string _detailsFor = string.Empty;

    public ReaderView()
    {
        Reader = ViewHost.Get<ReaderViewModel>();
        Look = ViewHost.Get<StyleState>();
        InitializeComponent();
        Reader.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(ReaderViewModel.EmailId), StringComparison.Ordinal) && !string.Equals(_detailsFor, Reader.EmailId, StringComparison.Ordinal))
            {
                SetDetails(false);
            }
        };
    }

    public ReaderViewModel Reader { get; }

    public StyleState Look { get; }

    private void OnDetailsClick(object sender, RoutedEventArgs e) => SetDetails(DetailsPanel.Visibility != Visibility.Visible);

    private void SetDetails(bool open)
    {
        _detailsFor = Reader.EmailId;
        DetailsPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        DetailsLabel.Text = open ? "Hide details" : "Details";
        DetailsChevron.Kind = open ? "ChevronDown" : "ChevronRight";
    }

    // "Try again" opens the selected message once more (spec I-230).
    private void OnRetryClick(object sender, RoutedEventArgs e) => _list.OpenSelected(OpenReason.Click);
}
