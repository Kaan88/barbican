// Mirrors: Bulwark lib/plugin-sandbox/open-task.ts:21-50 requestOpenTask (taskId; ui:tasks or tasks:read, checked per call; at most 20 a minute per add-on, TASK_OPENS_PER_MINUTE :23; resolves false when the calendar is not on
// screen or has not loaded the task) and host-api.ts:292,2126. The navigator opens the task's editor only while the calendar is the app on screen; it never switches apps for an add-on's task.
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Navigation;

internal sealed class PluginUiOpenTask(PluginNavigationHost host) : PluginNavigationMethod("ui.openTask", [["ui:tasks"], ["tasks:read"]], host)
{
    protected override string RateKind => "ui-open-task";

    protected override int RateLimit => 20;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        if (Host.Navigator is not { } navigator) return Answer(false);
        var opened = await navigator.OpenTaskAsync(capture!.LocalAccountKey, arguments.Item(0).StringValue, ct).ConfigureAwait(false);
        return Answer(opened);
    }
}
