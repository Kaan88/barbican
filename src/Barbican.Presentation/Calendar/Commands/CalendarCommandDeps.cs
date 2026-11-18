// What the calendar commands need (owner cal-ui). One record so the registration and the tests build the command set the same way.

using Barbican.Core.Calendar.Editing;
using Barbican.Core.Calendar.Feeds;
using Barbican.Core.Calendar.Invitations;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Calendar.Editors;
using Barbican.Presentation.Calendar.Editors.Dialogs;
using Barbican.Presentation.Calendar.Grid;
using Barbican.Presentation.Calendar.Tasks;
using Barbican.Presentation.Common;
using Barbican.Presentation.Dialogs;

namespace Barbican.Presentation.Calendar.Commands;

/// <summary>The services of the calendar commands.</summary>
public sealed record CalendarCommandDeps(
    ICalendarViewModel View,
    CalendarEditors Editors,
    CalendarEditorHost Host,
    ICalendarSource Source,
    ICalendarWriter Writer,
    ICalendarAdmin Admin,
    ICalendarSubscriptions Subscriptions,
    IIcsImporter Importer,
    IInvitationService Invitations,
    ICalendarRules Rules,
    ICalendarFilePicker Picker,
    ICalendarChrome Chrome,
    TasksViewModel Tasks,
    TimeProvider Time,
    IClipboardService? Clipboard = null,
    IToastService? Toasts = null,
    IDialogService? Dialogs = null);
