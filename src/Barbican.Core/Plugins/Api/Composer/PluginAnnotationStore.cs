// Mirrors: lib/plugin-sandbox/host-api-checker.ts:90-101 (setAnnotations hands one checker's results to the composer) and checkers.ts:192-195 (a plugin turned off loses
// what it registered). Annotations from an on-demand checker; the typing path keeps its own state in the composer view. No Stalwart counterpart.
using System.Collections.Immutable;

namespace Barbican.Core.Plugins.Api.Composer;

/// <summary>Annotations plugins pushed with composer.setAnnotations, per composer and checker. A set replaces only that checker's list.</summary>
public sealed class PluginAnnotationStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(string Composer, string Package, string Checker), Entry> _entries = [];

    public event EventHandler? Changed;

    /// <summary>Replaces the annotations of one checker of one composer (an empty list clears them).</summary>
    public void Set(string composerId, PluginCheckerRegistration spec, long docVersion, IReadOnlyList<PluginCheckIssue> issues)
    {
        ArgumentException.ThrowIfNullOrEmpty(composerId);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(issues);
        lock (_gate)
        {
            var key = (composerId, spec.Owner.Package.Id, spec.Id);
            if (issues.Count == 0) _ = _entries.Remove(key);
            else _entries[key] = new(spec, docVersion, [.. issues]);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>What can still be drawn: the checker is registered by a live add-on and the text is the one that was checked.</summary>
    public IReadOnlyList<(PluginCheckerRegistration Spec, PluginCheckIssue Issue)> Current(string composerId, long docVersion, PluginCheckerRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        lock (_gate)
        {
            foreach (var pair in _entries.Where(pair => !pair.Value.Spec.Owner.IsAlive).ToArray()) _ = _entries.Remove(pair.Key);
            return [.. _entries.Where(pair => string.Equals(pair.Key.Composer, composerId, StringComparison.Ordinal) &&
                    pair.Value.DocVersion == docVersion && registry.Contains(pair.Value.Spec))
                .OrderBy(pair => pair.Value.Spec.Id, StringComparer.Ordinal)
                .SelectMany(pair => pair.Value.Issues.Select(issue => (pair.Value.Spec, issue)))];
        }
    }

    /// <summary>Forgets everything of a closed composer.</summary>
    public void Forget(string composerId)
    {
        var any = false;
        lock (_gate)
            foreach (var key in _entries.Keys.Where(key => string.Equals(key.Composer, composerId, StringComparison.Ordinal)).ToArray())
                any |= _entries.Remove(key);
        if (any) Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed record Entry(PluginCheckerRegistration Spec, long DocVersion, ImmutableArray<PluginCheckIssue> Issues);
}
