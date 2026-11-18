// Mirrors: M4 plan section 4.5 (the editors' dependencies). Owner cal-ui.

using Barbican.Core.Calendar.Editing;
using Barbican.Core.Plugins.Hooks;
using Barbican.Presentation.Actions;

namespace Barbican.Presentation.Calendar.Editors;

/// <summary>What the event editor needs besides the event.</summary>
public sealed record EventEditorServices(
    IEditorSource Source,
    ICalendarWriter Writer,
    ICalendarRules Rules,
    ICalendarQuestions Questions,
    IAvailabilityService? Availability = null,
    IContactCardReader? Contacts = null,
    IHookBus? Hooks = null,
    IToastService? Toasts = null,
    TimeProvider? Time = null,
    bool TwelveHour = false);
