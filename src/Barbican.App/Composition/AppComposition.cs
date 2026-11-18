using Barbican.App.Composition.Features;
using Barbican.App.MailBody;
using Barbican.Core.Accounts;
using Barbican.Core.Calendar;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Compose;
using Barbican.Core.Contacts;
using Barbican.Core.Editor;
using Barbican.Core.Files;
using Barbican.Core.Jmap;
using Barbican.Presentation.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
#if AUTOMATION
using Barbican.Automation;
#endif

namespace Barbican.App.Composition;

/// <summary>
/// The composition root (docs/milestones/M1-plan.md §3.3), frozen after stage F1: it builds an empty
/// Microsoft.Extensions.Hosting host (no configuration files, no file watchers, nothing on the
/// cold-start path that the App does not use) and calls each feature's registration exactly once,
/// in this order, so a later feature's <c>Replace</c> wins over an earlier <c>TryAdd</c>. No stream
/// edits this file; each owns its own Add method.
/// </summary>
public static class AppComposition
{
    public static IHost Build(HiddenWindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        var services = builder.Services;
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(options);

        services.AddBarbicanCore();         // jmap:     src/Barbican.Core/Jmap/JmapRegistration.cs
        services.AddBarbicanMailCore();     // mailcore: src/Barbican.Presentation/Composition/MailPresentationRegistration.cs
        services.AddBarbicanAccounts();     // accounts: src/Barbican.Core/Accounts/AccountsRegistration.cs
        services.AddBarbicanPlatform();     // accounts: src/Barbican.App/Composition/PlatformServices.cs
        services.AddBarbicanViews();        // shell-ui: src/Barbican.App/Composition/ViewServices.cs
        services.AddBarbicanMailBody();     // body:     src/Barbican.App/MailBody/MailBodyRegistration.cs
        services.AddBarbicanMailActions();  // M2 actions: src/Barbican.App/Composition/Features/ActionsRegistration.cs (also registers the defaults of every M2 seam)
        services.AddBarbicanMenus();        // M2 menus:   src/Barbican.App/Composition/Features/MenusRegistration.cs
        services.AddBarbicanReaderDepth();  // M2 reader:  src/Barbican.App/Composition/Features/ReaderRegistration.cs
        services.AddBarbicanSearch();       // M2 search:  src/Barbican.App/Composition/Features/SearchRegistration.cs
        services.AddBarbicanUnified();      // M2 unified: src/Barbican.App/Composition/Features/UnifiedRegistration.cs
        services.AddBarbicanSendPipeline(); // M3 send-pipeline: src/Barbican.Core/Compose/SendPipelineRegistration.cs
        services.AddBarbicanEditorCore();   // M3 editor-core:   src/Barbican.Core/Editor/EditorCoreRegistration.cs
        services.AddBarbicanEditorView();   // M3 editor-view:   src/Barbican.App/Composition/Features/EditorViewRegistration.cs
        services.AddBarbicanComposer();     // M3 composer-ui:   src/Barbican.App/Composition/Features/ComposerRegistration.cs
        services.AddBarbicanCalendarData();             // M4 cal-data: src/Barbican.Core/Calendar/CalendarDataRegistration.cs
        services.AddBarbicanCalendarEditing();          // M4 cal-edit: src/Barbican.Core/Calendar/Editing/CalendarEditingRegistration.cs
        services.AddBarbicanCalendarDataViewModels();   // M4 cal-data: src/Barbican.Presentation/Composition/CalendarDataPresentationRegistration.cs
        services.AddBarbicanCalendarEditingViewModels(); // M4 cal-edit: src/Barbican.Presentation/Composition/CalendarEditingPresentationRegistration.cs
        services.AddBarbicanCalendarViews();            // M4 cal-views: src/Barbican.App/Composition/Features/CalendarViewsRegistration.cs
        services.AddBarbicanCalendarUi();               // M4 cal-ui: src/Barbican.App/Composition/Features/CalendarUiRegistration.cs
        services.AddBarbicanContactsData();            // M5 con-data: src/Barbican.Core/Contacts/ContactsDataRegistration.cs
        services.AddBarbicanFilesData();               // M5 files-data: src/Barbican.Core/Files/FilesDataRegistration.cs
        services.AddBarbicanContactsUi();              // M5 con-ui: src/Barbican.App/Composition/Features/ContactsUiRegistration.cs
        services.AddBarbicanFilesUi();                 // M5 files-ui: src/Barbican.App/Composition/Features/FilesUiRegistration.cs
#if AUTOMATION
        services.AddBarbicanAutomation();   // F1:       src/Barbican.Automation/AutomationRegistration.cs
#endif
        return builder.Build();
    }
}
