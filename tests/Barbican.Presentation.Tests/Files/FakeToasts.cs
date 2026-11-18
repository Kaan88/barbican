// Collects the toasts the Files actions show (tests only). Not a Rust row.

using Barbican.Presentation.Actions;

namespace Barbican.Presentation.Tests.Files;

internal sealed class FakeToasts : IToastService
{
    public List<ToastRequest> Shown { get; } = [];

    public ToastHandle Show(ToastRequest toast)
    {
        Shown.Add(toast);
        return new ToastHandle(Shown.Count);
    }

    public void Dismiss(ToastHandle handle)
    {
    }
}
