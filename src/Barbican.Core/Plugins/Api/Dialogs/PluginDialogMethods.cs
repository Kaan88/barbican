// Mirrors Bulwark lib/plugin-sandbox/host-api.ts:219-221 (no permission) and 2236-2290 (ui.confirm, ui.alert, ui.prompt: options object, text only where text is expected),
// host-dialog.ts:56-155 (cut limits, one dialog per add-on, 20 per minute, both refused with an error) and runtime.tsx:800-816 (no timeout, results).
// ui.openDialog shares the same limiter (host-dialog.ts enqueueDialog counts custom dialogs with the rest): see PluginOpenDialogMethod.
using System.Globalization;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Dialogs;

/// <summary>ui.alert, ui.confirm, ui.prompt and ui.openDialog: native dialogs attributed to the add-on by the host presenter.</summary>
public static class PluginDialogMethods
{
    /// <summary>Methods created: alert, confirm, prompt, openDialog.</summary>
    public const int MethodCount = 4;

    /// <summary>Bulwark MAX_DIALOGS_PER_MINUTE.</summary>
    public const int MaxPerMinute = 20;

    /// <summary>How often an open dialog re-checks that its call is still current.</summary>
    public static readonly TimeSpan CurrentCheckInterval = TimeSpan.FromSeconds(1);

    /// <summary>Creates the three methods over one shared per-add-on limiter. <paramref name="present"/> draws the request and returns when it ends; it must end the dialog when its token is cancelled.</summary>
    public static IReadOnlyList<IPluginApiMethod> Create(TimeProvider time,
        Func<PluginDialogRequest, IPluginInvocationContext, CancellationToken, Task<PluginDialogOutcome>> present,
        Func<PluginSlotDialogRequest, IPluginInvocationContext, CancellationToken, Task<PluginSlotDialogOutcome>>? presentSlot = null)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(present);
        var gate = new Gate(time);
        return [new Method("ui.alert", PluginDialogKind.Alert, gate, time, present),
            new Method("ui.confirm", PluginDialogKind.Confirm, gate, time, present),
            new Method("ui.prompt", PluginDialogKind.Prompt, gate, time, present),
            new PluginOpenDialogMethod(gate, time, presentSlot)];
    }

    /// <summary>Applies Bulwark's defaults, text-only reads and limits to a call's first argument; anything that is not an object reads as no options.</summary>
    public static PluginDialogRequest Read(PluginDialogKind kind, PluginValue arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        PluginValue? options = arguments is { Kind: PluginValueKind.Array, Count: > 0 } && arguments.Item(0) is { Kind: PluginValueKind.Object } first ? first : null;
        var fields = new List<PluginDialogField>();
        if (kind == PluginDialogKind.Prompt && Get(options, "fields") is { Kind: PluginValueKind.Array } list)
        {
            for (var i = 0; i < list.Count && fields.Count < PluginDialogText.MaxFields; i++)
            {
                if (list.Item(i) is not { Kind: PluginValueKind.Object } item || Text(item, "name") is not { } name) continue;
                fields.Add(new PluginDialogField(PluginDialogText.CutKey(name),
                    PluginDialogText.Clean(Text(item, "label") ?? string.Empty, PluginDialogText.MaxLabel, multiline: false),
                    string.Equals(Text(item, "type"), "password", StringComparison.Ordinal),
                    Label(Text(item, "placeholder")), Truthy(Get(item, "required"))));
            }
        }

        return new PluginDialogRequest(kind,
            Label(Text(options, "title"), PluginDialogText.MaxTitle),
            PluginDialogText.Clean(Text(options, "message") ?? string.Empty, PluginDialogText.MaxMessage, multiline: true),
            Label(Text(options, "confirmLabel")),
            kind == PluginDialogKind.Alert ? null : Label(Text(options, "cancelLabel")),
            kind == PluginDialogKind.Confirm && Truthy(Get(options, "danger")),
            fields);
    }

    private static string? Label(string? text, int max = PluginDialogText.MaxLabel)
    {
        if (text is null) return null;
        var clean = PluginDialogText.Clean(text, max, multiline: false);
        return clean.Length == 0 ? null : clean;
    }

    private static PluginValue? Get(PluginValue? value, string name) =>
        value is not null && value.TryGetProperty(name, out var property) ? property : null;

    private static string? Text(PluginValue? value, string name) => Get(value, name) is { Kind: PluginValueKind.String } text ? text.StringValue : null;

    // JavaScript truthiness of a structured-clone value, for Bulwark's `!!opts.danger` and `!!f.required`.
    private static bool Truthy(PluginValue? value) => value is null ? false : value.Kind switch
    {
        PluginValueKind.Undefined or PluginValueKind.Null => false,
        PluginValueKind.Boolean => value.BooleanValue,
        PluginValueKind.String => value.StringValue.Length > 0,
        PluginValueKind.Number => double.TryParse(value.NumberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number != 0 && !double.IsNaN(number),
        PluginValueKind.BigInt => !string.Equals(value.NumberText, "0", StringComparison.Ordinal),
        _ => true,
    };

    internal sealed class Method(string name, PluginDialogKind kind, Gate gate, TimeProvider time,
        Func<PluginDialogRequest, IPluginInvocationContext, CancellationToken, Task<PluginDialogOutcome>> present) : IPluginApiMethod
    {
        // No permission and no deadline: the call waits for the user (Bulwark host-api.ts:219-221, runtime.tsx:803-816).
        public PluginApiDescriptor Descriptor { get; } = new(name, [], Timeout.InfiniteTimeSpan);

        public PluginErrorCode? ValidateArguments(PluginValue arguments) => null;

        public async Task<PluginCallOutcome> InvokeAsync(IPluginInvocationContext context, PluginValue arguments, CancellationToken ct)
        {
            var request = Read(kind, arguments);
            if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return PluginCallOutcome.Failed(PluginErrorCode.StaleContext);
            var owner = (context.Binding.Profile, context.Binding.Package.Id);
            if (gate.TryOpen(owner) is { } refused) return PluginCallOutcome.Failed(refused);
            try
            {
                using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var stale = 0;
                var watcher = Task.Run(() => WatchAsync(context, stop, () => Interlocked.Exchange(ref stale, 1), time), CancellationToken.None);
                PluginDialogOutcome outcome;
                try { outcome = await present(request, context, stop.Token).ConfigureAwait(false); }
                finally
                {
                    await stop.CancelAsync().ConfigureAwait(false);
                    await watcher.ConfigureAwait(false);
                }

                if (ct.IsCancellationRequested) return PluginCallOutcome.Failed(PluginErrorCode.Cancelled);
                if (outcome.Error is { } error) return PluginCallOutcome.Failed(error);
                if (Volatile.Read(ref stale) == 1 || !await context.CheckCurrentAsync(ct).ConfigureAwait(false))
                    return PluginCallOutcome.Failed(PluginErrorCode.StaleContext);
                return PluginCallOutcome.Succeeded(Result(kind, outcome));
            }
            finally
            {
                gate.Close(owner);
            }
        }

        internal static async Task WatchAsync(IPluginInvocationContext context, CancellationTokenSource stop, Action markStale, TimeProvider time)
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    await Task.Delay(CurrentCheckInterval, time, stop.Token).ConfigureAwait(false);
                    if (await context.CheckCurrentAsync(stop.Token).ConfigureAwait(false)) continue;
                    markStale();
                    await stop.CancelAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // the dialog ended first
            }
        }

        private static PluginValue? Result(PluginDialogKind kind, PluginDialogOutcome outcome)
        {
            var builder = new PluginValueBuilder();
            switch (kind)
            {
                case PluginDialogKind.Alert:
                    return null;
                case PluginDialogKind.Confirm:
                    return builder.Build(builder.Boolean(outcome.Confirmed));
                default:
                    if (!outcome.Confirmed || outcome.Values is null) return builder.Build(builder.Null());
                    return builder.Build(builder.Object([.. outcome.Values.Select(pair => (pair.Key, builder.String(pair.Value)))]));
            }
        }
    }

    // One open dialog per add-on and 20 openings per minute (Bulwark host-dialog.ts:122-143); the open check comes first and a refusal is not counted.
    internal sealed class Gate(TimeProvider time)
    {
        private readonly Lock _lock = new();
        private readonly Dictionary<(string Profile, string Plugin), (bool Open, Queue<long> Stamps)> _owners = [];

        internal PluginErrorCode? TryOpen((string Profile, string Plugin) owner)
        {
            lock (_lock)
            {
                var now = time.GetTimestamp();
                foreach (var (key, state) in _owners.ToArray())
                {
                    while (state.Stamps.TryPeek(out var start) && time.GetElapsedTime(start, now) >= TimeSpan.FromMinutes(1)) _ = state.Stamps.Dequeue();
                    if (!state.Open && state.Stamps.Count == 0) _ = _owners.Remove(key);
                }

                if (!_owners.TryGetValue(owner, out var current))
                {
                    if (_owners.Count >= 256) return PluginErrorCode.RateLimited;
                    current = (false, new Queue<long>());
                }

                if (current.Open) return PluginErrorCode.Conflict;
                if (current.Stamps.Count >= MaxPerMinute) return PluginErrorCode.RateLimited;
                current.Stamps.Enqueue(now);
                _owners[owner] = (true, current.Stamps);
                return null;
            }
        }

        internal void Close((string Profile, string Plugin) owner)
        {
            lock (_lock)
            {
                if (_owners.TryGetValue(owner, out var state)) _owners[owner] = (false, state.Stamps);
            }
        }
    }
}
