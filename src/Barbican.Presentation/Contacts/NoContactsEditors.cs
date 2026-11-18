// Mirrors: see IContactsEditors. The default until the App registers the form dialog (and in tests). Owner con-ui.

using Barbican.Core.Contacts;

namespace Barbican.Presentation.Contacts;

/// <summary>An editor host that opens nothing.</summary>
public sealed class NoContactsEditors : IContactsEditors
{
    public Task OpenNewAsync(string? bookId, ContactForm? seed, string? email, CancellationToken ct) => Task.CompletedTask;

    public Task<bool> OpenEditAsync(string cardId, CancellationToken ct) => Task.FromResult(false);
}
