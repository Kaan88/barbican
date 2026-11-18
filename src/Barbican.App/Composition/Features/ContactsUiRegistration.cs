// Mirrors: M5 plan section 4.10 (DI). Owner con-ui: the contacts screen (registered as an IAppScreenFactory, section 4.12), the form and dialog contents, the system pickers, the screen's part of the keys, the hidden-run commands and sample data.

using Barbican.App.Shell;
using Barbican.App.Views.Contacts;
using Barbican.App.Views.Dialogs;
using Barbican.Presentation.Composition;
using Barbican.Presentation.Contacts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.App.Composition.Features;

/// <summary>The contacts screen (M5 stream con-ui).</summary>
public static class ContactsUiRegistration
{
    public static IServiceCollection AddBarbicanContactsUi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
#if AUTOMATION
        // a sample-data run (BARBICAN_SAMPLE=1) shows made-up contacts without signing in: the account, store, writer and address books are one in-memory object
        if (string.Equals(Environment.GetEnvironmentVariable("BARBICAN_SAMPLE"), "1", StringComparison.Ordinal))
        {
            services.TryAddSingleton<Views.Contacts.Automation.SampleContactStore>();
            services.Replace(ServiceDescriptor.Singleton<Barbican.Core.Contacts.IContactStore>(sp => sp.GetRequiredService<Views.Contacts.Automation.SampleContactStore>()));
            services.Replace(ServiceDescriptor.Singleton<Barbican.Core.Contacts.IContactWriter>(sp => sp.GetRequiredService<Views.Contacts.Automation.SampleContactStore>()));
            services.Replace(ServiceDescriptor.Singleton<Barbican.Core.Contacts.IAddressBooks>(sp => sp.GetRequiredService<Views.Contacts.Automation.SampleContactStore>()));
            services.Replace(ServiceDescriptor.Singleton<IContactsAccountSource, Views.Contacts.Automation.SampleContactsAccount>());
            services.Replace(ServiceDescriptor.Singleton<Barbican.Core.Contacts.IContactDirectory, Views.Contacts.Automation.SampleContactDirectory>());
            services.RemoveAll<Barbican.Core.Contacts.IContactPhotos>(); // the sample account has no server: a picked photo goes into the card as a data: URI
        }
#endif
        _ = services.AddBarbicanContactsUiViewModels();
        services.Replace(ServiceDescriptor.Singleton<IContactsChrome, ContactsScreenChrome>());
        services.Replace(ServiceDescriptor.Singleton(_ => new ContactsFilePicker(ContactsSystemPicker.OpenVcardAsync, ContactsSystemPicker.OpenImageAsync, ContactsSystemPicker.SaveTextAsync)));
        services.AddSingleton<IAppScreenFactory, ContactsScreenFactory>();
        services.AddSingleton<IDialogContent, ContactsFormDialogContent>();
        services.AddSingleton<IDialogContent, ContactsGroupDialogContent>();
        services.AddSingleton<IDialogContent, ContactsImportDialogContent>();
        services.AddSingleton<IDialogContent, ContactsMergeDialogContent>();
        services.AddSingleton<IDialogContent, ContactsDirectoryDialogContent>();
        services.AddSingleton<IDialogContent, ContactsPrintDialogContent>();
#if AUTOMATION
        for (var i = 0; i < Views.Contacts.Automation.ContactsCommand.Count; i++)
        {
            var index = i;
            services.AddSingleton<Barbican.Automation.IAutomationCommand>(sp => Views.Contacts.Automation.ContactsCommand.Create(sp).ElementAt(index));
        }
#endif
        return services;
    }
}
