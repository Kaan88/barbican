// Test double for IMessageOpener. Owner mailcore.

using Barbican.Presentation.Mail;

namespace Barbican.Presentation.Tests.Support;

internal sealed class RecordingOpener : IMessageOpener
{
    public List<(string Id, OpenReason Reason, ListDirection Direction)> Opened { get; } = [];

    public int Closed { get; private set; }

    public void Open(MessageRowViewModel row, OpenReason reason, ListDirection direction) => Opened.Add((row.Id, reason, direction));

    public void Close() => Closed++;
}
