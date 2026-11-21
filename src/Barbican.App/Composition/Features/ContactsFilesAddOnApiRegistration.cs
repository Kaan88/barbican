#if WINDOWS
// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:178-186 (contact.* and addressbook.*) and assistant/d.ts (files.*). Registers the scoped contact, address-book and files add-on methods over the stores, writers and transfer
// service the Contacts and Files views use, so hooks run and the open views update. Windows head only, like the other add-on host methods.
using Barbican.Core.Contacts;
using Barbican.Core.Files;
using Barbican.Core.Plugins.Api.Contacts;
using Barbican.Core.Plugins.Api.Files;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.App.Composition.Features;

internal static class ContactsFilesAddOnApiRegistration
{
    /// <summary>The 8 contact and address-book methods (contacts:read / contacts:write) and the 8 files methods (files:read / files:write); each needs its own grant for the login it runs on.</summary>
    internal static IServiceCollection AddContactsFilesAddOnApi(this IServiceCollection services)
    {
        services.AddSingleton<IPluginContactsHost>(sp => new PluginContactsHost(sp.GetRequiredService<IContactStore>(), sp.GetRequiredService<IContactWriter>(), sp.GetRequiredService<IAddressBooks>()));
        services.AddSingleton<IPluginFilesHost>(sp => new PluginFilesHost(sp.GetRequiredService<IFileStore>(), sp.GetRequiredService<IFileWriter>(), sp.GetRequiredService<IFileTransfer>(), sp.GetRequiredService<IPluginMailRateLimiter>()));
        for (var index = 0; index < PluginContactsMethods.Count; index++)
        {
            var method = index;
            services.AddSingleton<IPluginApiMethod>(sp => PluginContactsMethods.Create(sp.GetRequiredService<IPluginContactsHost>())[method]);
        }

        for (var index = 0; index < PluginFilesMethods.Count; index++)
        {
            var method = index;
            services.AddSingleton<IPluginApiMethod>(sp => PluginFilesMethods.Create(sp.GetRequiredService<IPluginFilesHost>())[method]);
        }

        return services;
    }
}
#endif
