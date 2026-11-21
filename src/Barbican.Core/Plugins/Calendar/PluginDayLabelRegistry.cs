// Mirrors: Bulwark lib/plugin-sandbox/day-labels.ts:46-90 (byPlugin, setDayLabels replaces an add-on's labels and an empty list removes them, clearDayLabels, dayLabelsByDate, subscribeDayLabels).
// Barbican additions: the labels belong to a live runtime binding (an add-on that is disabled, revoked or removed loses them: the binding's lifetime ends, which removes them), a newer grant generation is never replaced by an
// older runtime, and the add-on's name comes from a resolver the app supplies.
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Core.Plugins.Calendar;

/// <summary>The labels the add-ons put on calendar days. Thread safe; <see cref="Changed"/> may fire on any thread.</summary>
public sealed class PluginDayLabelRegistry(Func<string, string>? nameOf = null)
{
    private readonly Lock _gate = new();
    private readonly List<Owned> _owners = [];
    private readonly Func<string, string> _nameOf = nameOf ?? (id => id);

    /// <summary>Labels were set, cleared or lost their add-on.</summary>
    public event EventHandler? Changed;

    /// <summary>Every label of every live add-on, in the order the add-ons first set labels.</summary>
    public IReadOnlyList<RegisteredPluginDayLabel> All
    {
        get
        {
            lock (_gate)
            {
                foreach (var dead in _owners.Where(o => !o.Binding.IsAlive).ToArray()) Drop(dead);
                return [.. _owners.SelectMany(o => o.Labels.Select(l => new RegisteredPluginDayLabel(o.Binding.Package.Id, _nameOf(o.Binding.Package.Id), l)))];
            }
        }
    }

    /// <summary>Every label of every live add-on on <paramref name="day"/>.</summary>
    public IReadOnlyList<RegisteredPluginDayLabel> On(DateOnly day) => [.. All.Where(l => l.Label.Date == day)];

    /// <summary>Replaces the labels of the add-on that owns <paramref name="owner"/>; an empty list removes them. False when the runtime is dead or older than the one that set labels before.</summary>
    public bool Set(PluginRuntimeBinding owner, IReadOnlyList<PluginDayLabel> labels)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(labels);
        if (labels.Count > PluginDayLabelChecks.MaxLabels) return false;
        lock (_gate)
        {
            if (!owner.IsAlive) return false;
            var existing = _owners.FindIndex(o => string.Equals(o.Binding.Package.Id, owner.Package.Id, StringComparison.Ordinal));
            if (existing >= 0 && _owners[existing].Binding.IsAlive && _owners[existing].Binding.GrantGeneration > owner.GrantGeneration) return false;
            if (existing >= 0)
            {
                var old = _owners[existing];
                if (labels.Count == 0) Drop(old);
                else
                {
                    _ = old.Watch.Unregister();
                    _owners[existing] = new Owned(owner, [.. labels], Watch(owner));
                }
            }
            else if (labels.Count > 0) _owners.Add(new Owned(owner, [.. labels], Watch(owner)));
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Drops the labels the binding owns (its runtime ended).</summary>
    public void Remove(PluginRuntimeBinding owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        bool removed;
        lock (_gate)
        {
            var entry = _owners.FirstOrDefault(o => ReferenceEquals(o.Binding, owner));
            removed = entry is not null;
            if (entry is not null) Drop(entry);
        }

        if (removed) Changed?.Invoke(this, EventArgs.Empty);
    }

    private CancellationTokenRegistration Watch(PluginRuntimeBinding owner) => owner.Lifetime.Register(() => Remove(owner));

    private void Drop(Owned entry)
    {
        _ = _owners.Remove(entry);
        _ = entry.Watch.Unregister();
    }

    private sealed record Owned(PluginRuntimeBinding Binding, IReadOnlyList<PluginDayLabel> Labels, CancellationTokenRegistration Watch);
}
