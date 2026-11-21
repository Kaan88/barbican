// Mirrors: Bulwark lib/plugin-sandbox/calendar-navigation.ts:28-60 requestCalendarView ({view} only; month, week, day and agenda need ui:calendar-action or calendar:read, tasks needs ui:tasks or tasks:read, checked per call;
// false when nothing could take the move or tasks are off; at most 10 moves a minute per add-on, CALENDAR_OPENS_PER_MINUTE :35) and host-api.ts:290,2125. Only the calendar's own views: an add-on can never send the app anywhere else.
// Barbican's static gate is the union of the four permissions (the dispatcher refuses a grant with none); the per-view group is then demanded for the captured login.
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Navigation;

internal sealed class PluginUiOpenCalendar(PluginNavigationHost host)
    : PluginNavigationMethod("ui.openCalendar", [["ui:calendar-action"], ["calendar:read"], ["ui:tasks"], ["tasks:read"]], host)
{
    private static readonly string[] Keys = ["view"];

    protected override string RateKind => "ui-open-calendar";

    protected override int RateLimit => 10;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginMailArgs.TryOptions(PluginMailArgs.At(arguments, 0), Keys, true, out var options) && TryView(PluginMailArgs.Prop(options, "view"), out _) ? null : PluginErrorCode.InvalidArguments;

    protected override bool Holds(IPluginInvocationContext context, string login, PluginValue arguments)
    {
        _ = TryView(PluginMailArgs.Prop(PluginMailArgs.At(arguments, 0), "view"), out var view);
        string[] needed = view == PluginCalendarView.Tasks ? ["ui:tasks", "tasks:read"] : ["ui:calendar-action", "calendar:read"];
        return needed.Any(permission => context.Demand(permission, login).IsOk);
    }

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        if (Host.Navigator is not { } navigator) return Answer(false);
        _ = TryView(PluginMailArgs.Prop(PluginMailArgs.At(arguments, 0), "view"), out var view);
        var moved = await navigator.OpenCalendarAsync(capture!.LocalAccountKey, view, ct).ConfigureAwait(false);
        return Answer(moved);
    }

    private static bool TryView(PluginValue? value, out PluginCalendarView view)
    {
        view = default;
        if (value is not { Kind: PluginValueKind.String }) return false;
        switch (value.StringValue)
        {
            case "month": view = PluginCalendarView.Month; return true;
            case "week": view = PluginCalendarView.Week; return true;
            case "day": view = PluginCalendarView.Day; return true;
            case "agenda": view = PluginCalendarView.Agenda; return true;
            case "tasks": view = PluginCalendarView.Tasks; return true;
            default: return false;
        }
    }
}
