// Mirrors: M5 plan section 4.12 (the shell seam: ShellPage shows the screen of the factory registered for the rail's active app, built on the first visit); PORT-PLAN section 5 M5 (Contacts needs urn:ietf:params:jmap:contacts, I-001).

using Barbican.App.Shell;
using Barbican.Presentation.Contacts;
using Barbican.Presentation.Shell;

namespace Barbican.App.Views.Contacts;

/// <summary>The Contacts app of the rail.</summary>
public sealed class ContactsScreenFactory(IContactsAccountSource account) : IAppScreenFactory
{
    private ContactsScreen? _screen;

    public AppId App => AppId.Contacts;

    public bool IsAvailable() => account.HasContacts;

    public UIElement Create() => _screen ??= new ContactsScreen();

    public void OnShown() => _screen?.OnShown();
}
