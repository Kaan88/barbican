// Mirrors: M2 plan section 3.3. Owner search (wave 2): IListQueryShaper (Replace), ISnippetSource, the search commands, IPaletteProvider for global search, the quick rule commands, the Rules menu
// providers (the later registration of MENU-MAIL>more and MENU-FOLDER wins over the menus stream's, which this one decorates). The slot filler for the search box is the one stage F registered.

using Barbican.App.Shell;
using Barbican.App.Views.Search;
#if AUTOMATION
using Barbican.App.Views.Search.Automation;
#endif
using Barbican.Core.Contacts;
using Barbican.Core.Files;
using Barbican.Core.Mail.Lists;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Service;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Search;
using Barbican.Core.Storage;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Calendar.Grid;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Contacts;
using Barbican.Presentation.Files;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Menus;
using Barbican.Presentation.Palette;
using Barbican.Presentation.Rules;
using Barbican.Presentation.Search;
using Barbican.Presentation.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.App.Composition.Features;

/// <summary>Search syntax, chips, suggestions, history, snippets, filter panel, global search, quick rules (M2 stream search).</summary>
public static class SearchRegistration
{
    public static IServiceCollection AddBarbicanSearch(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ISlotContent>(new SlotContent(SlotNames.ListSearch, () => new SearchBoxView()));
#if AUTOMATION
        services.AddBarbicanSearchAutomation();
#endif
        return services.AddSearchServices().AddSearchCommands().AddRuleMenus();
    }

    private static ActiveClient Active(IServiceProvider sp) => new(sp.GetRequiredService<IActiveMail>());

    private static IServiceCollection AddSearchServices(this IServiceCollection services)
    {
        services.TryAddSingleton(sp => new SearchHistoryStore(sp.GetRequiredService<IProfilePaths>().ConfigDir));
        // a unified row's snippet is asked of its own login (Fable review 3 R3-01)
        services.TryAddSingleton(sp => new SearchSnippetSource(() => Active(sp).Client, login => sp.GetService<Barbican.Presentation.Unified.IUnifiedLogins>()?.ClientOf(login)));
        services.TryAddSingleton<ISnippetSource>(sp => sp.GetRequiredService<SearchSnippetSource>());
        services.Replace(ServiceDescriptor.Singleton<IListQueryShaper>(sp => new SearchListQueryShaper(() => Active(sp).Mailboxes, sp.GetRequiredService<SearchSnippetSource>())));
        // a hit of another app opens in it: the shell switches the app, the app's own view model shows the place (the services are optional: a head without that app opens nothing)
        services.TryAddSingleton(sp => new SearchHitOpener(
            sp.GetRequiredService<MessageListViewModel>(),
            sp.GetRequiredService<SidebarViewModel>(),
            sp.GetService<ShellViewModel>(),
            sp.GetService<IContactsViewModel>(),
            sp.GetService<ICalendarViewModel>(),
            sp.GetService<IFilesViewModel>()));
        // contacts, events and files of the active login come from their stores (sample stores in a hidden run)
        services.TryAddSingleton<IGlobalSearchSources>(sp => new StoreSearchSources(
            () => sp.GetRequiredService<IActiveMail>().AccountKey,
            sp.GetService<IContactStore>(),
            sp.GetService<ICalendarSource>(),
            sp.GetService<IFileStore>(),
            sp.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton(sp => new SearchBoxModel(
            sp.GetRequiredService<MessageListViewModel>().SearchAsync,
            sp.GetRequiredService<SearchHistoryStore>(),
            () => People(sp.GetRequiredService<MessageListViewModel>())));
        return services;
    }

    private static IReadOnlyList<(string Name, string Email)> People(MessageListViewModel list) =>
        [.. list.Messages.SelectMany(m => m.Summary.From).Where(a => a.Email.Length > 0).Select(a => (a.Name ?? string.Empty, a.Email))];

    private static IServiceCollection AddSearchCommands(this IServiceCollection services)
    {
        services.AddSingleton<IPaletteProvider>(sp =>
        {
            var directory = sp.GetRequiredService<IMailboxDirectory>();
            var list = sp.GetRequiredService<MessageListViewModel>();
            return new GlobalSearchPaletteProvider(
                sp.GetRequiredService<IActiveMail>(),
                sp.GetRequiredService<IHookBus>(),
                id => directory.Tree.All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal))?.Name ?? string.Empty,
                () => list.MailboxId ?? string.Empty,
                sp.GetRequiredService<IGlobalSearchSources>());
        });
        foreach (var i in Enumerable.Range(0, 2))
        {
            var index = i;
            services.AddSingleton<IAppCommand>(sp => SearchCommands.Create(new Lazy<PaletteController>(() => sp.GetRequiredService<PaletteController>()), sp.GetRequiredService<SearchHitOpener>())[index]);
        }

        services.TryAddSingleton(sp => new QuickRuleService(sp.GetRequiredService<IActiveMail>(), sp.GetRequiredService<IMailboxDirectory>(), sp.GetService<IToastService>()));
        foreach (var i in Enumerable.Range(0, QuickRuleCommands.Count))
        {
            var index = i;
            services.AddSingleton<IAppCommand>(sp => QuickRuleCommands.Create(sp.GetRequiredService<QuickRuleService>(), id => TagOf(sp.GetRequiredService<IMenuDataSource>(), id))[index]);
        }

        return services;
    }

    private static MenuTag? TagOf(IMenuDataSource data, string id) =>
        data.Tags().FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));

    private static IServiceCollection AddRuleMenus(this IServiceCollection services)
    {
        foreach (var i in Enumerable.Range(0, RuleMenus.Count))
        {
            var index = i;
            services.AddSingleton<IMenuProvider>(sp =>
            {
                var directory = sp.GetRequiredService<IMailboxDirectory>();
                return RuleMenus.Providers(sp.GetRequiredService<IMenuDataSource>(), _ => directory.Tree.All.Any(m => m.Role == MailboxRole.Junk))[index];
            });
        }

        return services;
    }

    private readonly record struct ActiveClient(IActiveMail Mail)
    {
        public Core.Jmap.IJmapClient? Client => (Mail.Mail as MailService)?.Client;

        public IReadOnlyList<System.Text.Json.Nodes.JsonObject>? Mailboxes => (Mail.Mail as MailService)?.LastMailboxes?.List;
    }
}
