// Test double for IMessageNeighbours: a fixed list of rows. Owner body.

using Barbican.Presentation.Mail;
using Barbican.Presentation.Mail.Loading;

namespace Barbican.Presentation.Tests.Mail.Loading;

internal sealed class StaticNeighbours : IMessageNeighbours
{
    public List<MessageRowViewModel> List { get; } = [];

    public IReadOnlyList<MessageRowViewModel> Rows => List;

    public event EventHandler? ListLoaded;

    public void RaiseLoaded() => ListLoaded?.Invoke(this, EventArgs.Empty);
}
