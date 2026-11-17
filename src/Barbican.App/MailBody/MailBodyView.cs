// Mirrors: PORT-PLAN.md §3.6 (the mail body view: WebView2 on Windows, WebKitGTK overlay on Linux); Rust src/mailview.rs.
// Contract stub from F2 (M1-plan §3.3, §4.4); owner body. shell-ui places this type in ReaderView.xaml's body slot; body replaces the stub.

namespace Barbican.App.MailBody;

/// <summary>The reader's message body view. A stub until the body stream builds the WebView host.</summary>
public sealed class MailBodyView : UserControl
{
}
