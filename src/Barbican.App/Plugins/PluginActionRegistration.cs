using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
using Barbican.Core.Plugins.Slots;

namespace Barbican.App.Plugins;

/// <summary>Declarative actions registered by an exact running add-on frame.</summary>
internal sealed class PluginActionRegistration : IPluginApiMethod
{
    internal const string ComposerHeader = "composer-header-actions";

    /// <summary>RD-08: rows in the menu on selected message text (Bulwark components/email/reader-selection-menu.tsx:21; permissions ui:context-menu or ui:toolbar, spec/plugins/slots.csv).</summary>
    internal const string ReaderSelection = "reader-selection-menu";
    private readonly ConditionalWeakTable<PluginRuntimeBinding, Contributions> _actions = new();
    private readonly Lock _gate = new();

    internal event EventHandler? Changed;

    public PluginApiDescriptor Descriptor { get; } = new("ui.setActions", [], TimeSpan.FromSeconds(5));

    public PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        Parse(arguments) is null ? PluginErrorCode.InvalidArguments : null;

    public async Task<PluginCallOutcome> InvokeAsync(IPluginInvocationContext context, PluginValue arguments, CancellationToken ct)
    {
        if (Parse(arguments) is not { } parsed) return PluginCallOutcome.Failed(PluginErrorCode.InvalidArguments);
        var (slot, actions) = parsed;
        var binding = context.Binding;
        var permission = string.Equals(slot, ComposerHeader, StringComparison.Ordinal) ? "ui:composer-toolbar" : "ui:toolbar";
        var allowed = binding.Kind == PluginFrameKind.Background && (context.Demand(permission, null).IsOk ||
            (string.Equals(slot, ReaderSelection, StringComparison.Ordinal) && context.Demand("ui:context-menu", null).IsOk));
        if (!allowed ||
            !await context.CheckCurrentAsync(ct).ConfigureAwait(false) ||
            !binding.TryWriteWhileAlive(() =>
            {
                lock (_gate)
                    _actions.GetValue(binding, _ => new Contributions()).Slots[slot] =
                        [.. actions.Select(action => new PluginActionContribution(binding, slot, action))];
            }))
            return PluginCallOutcome.Failed(PluginErrorCode.PermissionDenied);
        Changed?.Invoke(this, EventArgs.Empty);
        return PluginCallOutcome.Succeeded();
    }

    internal IReadOnlyList<PluginActionContribution> Actions(string slot)
    {
        lock (_gate)
            return [.. _actions.Where(entry => entry.Key.IsAlive).SelectMany(entry => entry.Value.Slots.GetValueOrDefault(slot, []))
                .OrderBy(item => item.Action.Order).ThenBy(item => item.Owner.Package.Id, StringComparer.Ordinal)
                .ThenBy(item => item.Action.Id, StringComparer.Ordinal)];
    }

    internal bool Contains(PluginActionContribution item)
    {
        lock (_gate)
            return item.Owner.IsAlive && _actions.TryGetValue(item.Owner, out var choices) &&
                choices.Slots.TryGetValue(item.Slot, out var list) && list.Any(candidate => ReferenceEquals(candidate, item));
    }

    private static (string Slot, PluginAction[] Actions)? Parse(PluginValue args)
    {
        if (args is not { Kind: PluginValueKind.Array, Count: 2 } ||
            args.Item(0) is not { Kind: PluginValueKind.String, StringValue: ComposerHeader or "reader-actions" or ReaderSelection } slot ||
            args.Item(1) is not { Kind: PluginValueKind.Array, Count: <= 16 } array) return null;
        var actions = new List<PluginAction>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < array.Count; index++)
        {
            var item = array.Item(index);
            if (ReadAction(item) is not { } action || !ids.Add(action.Id)) return null;
            actions.Add(action);
        }
        return (slot.StringValue, [.. actions]);
    }

    private static PluginAction? ReadAction(PluginValue item)
    {
        if (item.Kind != PluginValueKind.Object) return null;
        string? id = null, label = null, icon = null;
        var order = 0d;
        var danger = false;
        foreach (var name in item.PropertyNames)
        {
            var value = item.Property(name);
            switch (name)
            {
                case "id" when value is { Kind: PluginValueKind.String, StringValue.Length: > 0 and <= 60 }:
                    id = value.StringValue;
                    if (!id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')) return null;
                    break;
                case "label" when value is { Kind: PluginValueKind.String, StringValue.Length: > 0 and <= 80 }:
                    label = value.StringValue;
                    if (string.IsNullOrWhiteSpace(label) || label.Any(char.IsControl)) return null;
                    break;
                case "icon" when value is { Kind: PluginValueKind.String, StringValue.Length: <= 40 }:
                    icon = value.StringValue;
                    break;
                case "order" when value.Kind == PluginValueKind.Number &&
                    double.TryParse(value.NumberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
                    double.IsFinite(number):
                    order = number;
                    break;
                case "danger" when value.Kind == PluginValueKind.Boolean:
                    danger = value.BooleanValue;
                    break;
                default: return null;
            }
        }
        return id is not null && label is not null ? new(id, label, icon, order, danger, [], [], []) : null;
    }

    private sealed class Contributions
    {
        public Dictionary<string, ImmutableArray<PluginActionContribution>> Slots { get; } = new(StringComparer.Ordinal);
    }
}
