// Mirrors: M1-plan §4.3 (list to reader). Contract stub from F2; owner mailcore. mailcore registers a header-only placeholder
// (TryAdd); body replaces it with MessageLoader (Rust msgload.rs) in wave 3.

namespace Barbican.Presentation.Mail;

/// <summary>Opens a message in the reader.</summary>
public interface IMessageOpener
{
    void Open(MessageRowViewModel row, OpenReason reason, ListDirection direction);

    void Close();
}
