using Barbican.App.Motion;
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
            if (!string.Equals(e.PropertyName, nameof(ReaderViewModel.EmailId), StringComparison.Ordinal))
            {
                return;
            }

            if (!string.Equals(_detailsFor, Reader.EmailId, StringComparison.Ordinal))
            {
                SetDetails(false);
            }

            RiseHeader();
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

    // The prototype's header transition (a short rise of the new subject and sender line), on the view token. The new text is already set, so the
    // header never blanks; while keys repeat the selection moves faster than any animation and it snaps (design/MOTION.md rule 4).
    private void RiseHeader()
    {
        var scale = MotionRuntime.Scale;
        var ms = scale.Rapid || string.IsNullOrEmpty(Reader.EmailId) ? 0 : scale.ViewMs;
        Rise.Run(SubjectText, SubjectShift, ms, fromOpacity: 0.55, fromY: 6);
        Rise.Run(SenderLine, SenderShift, ms, fromOpacity: 0.55, fromY: 6);
    }

    // "Try again" opens the selected message once more (spec I-230).
    private void OnRetryClick(object sender, RoutedEventArgs e) => _list.OpenSelected(OpenReason.Click);
}
