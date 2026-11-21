// Mirrors: Bulwark lib/plugin-sandbox/calendar-navigation.ts:20-26 (the views a plugin may open: month, week, day, agenda, tasks).
namespace Barbican.Core.Plugins.Api.Navigation;

/// <summary>The calendar views an add-on may ask for; never any other screen.</summary>
public enum PluginCalendarView
{
    Month,
    Week,
    Day,
    Agenda,
    Tasks,
}
