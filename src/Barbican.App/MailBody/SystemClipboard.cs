// Mirrors: Bulwark navigator.clipboard.writeText; the same WinUI DataTransfer call MailBodyView uses for "Copy code". Owner reader.

using Barbican.Presentation.Common;
using Windows.ApplicationModel.DataTransfer;

namespace Barbican.App.MailBody;

/// <summary>The platform clipboard.</summary>
public sealed class SystemClipboard : IClipboardService
{
    /// <inheritdoc />
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }
}
