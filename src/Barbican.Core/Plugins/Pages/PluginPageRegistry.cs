// Mirrors: Bulwark lib/plugin-sandbox/pages.ts:47-85 (byPlugin map in install order, setPluginPages replaces an add-on's pages, an empty list removes them, clearPluginPages, allPages, findPage, subscribePages).
// Barbican additions: the entries belong to a live runtime binding (an add-on that is disabled, revoked or removed loses its pages: the binding's lifetime ends, which removes them, and the IsAlive filter), a newer
// grant generation is never replaced by an older runtime, and the add-on's name comes from a resolver the app supplies.
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Core.Plugins.Pages;

/// <summary>The pages the installed add-ons offer, in install order. Thread safe; <see cref="Changed"/> may fire on any thread.</summary>
public sealed class PluginPageRegistry(Func<string, string>? nameOf = null)
{
    private readonly Lock _gate = new();
    private readonly List<Owned> _owners = [];
    private readonly Func<string, string> _nameOf = nameOf ?? (id => id);

    /// <summary>Pages were set, cleared or lost their add-on.</summary>
    public event EventHandler? Changed;

    /// <summary>Every page of every live add-on, add-ons in the order they first set pages, pages in the order they set them.</summary>
    public IReadOnlyList<RegisteredPluginPage> All
    {
        get
        {
            lock (_gate)
            {
                foreach (var dead in _owners.Where(o => !o.Binding.IsAlive).ToArray())
                {
                    Drop(dead);
                }

                return [.. _owners.SelectMany(o => o.Pages.Select(p => new RegisteredPluginPage(o.Binding.Package.Id, _nameOf(o.Binding.Package.Id), p)))];
            }
        }
    }

    /// <summary>The page <paramref name="pageId"/> of the add-on <paramref name="packageId"/>, or null (also when that add-on is gone).</summary>
    public RegisteredPluginPage? Find(string packageId, string pageId) =>
        All.FirstOrDefault(p => string.Equals(p.PackageId, packageId, StringComparison.Ordinal) && string.Equals(p.Page.Id, pageId, StringComparison.Ordinal));

    /// <summary>Replaces the pages of the add-on that owns <paramref name="owner"/>; an empty list removes them. False when the runtime is dead or older than the one that set pages before.</summary>
    public bool Set(PluginRuntimeBinding owner, IReadOnlyList<PluginPage> pages)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count > PluginPageChecks.MaxPagesPerAddOn) return false;
        lock (_gate)
        {
            if (!owner.IsAlive) return false;
            var existing = _owners.FindIndex(o => string.Equals(o.Binding.Package.Id, owner.Package.Id, StringComparison.Ordinal));
            if (existing >= 0 && _owners[existing].Binding.IsAlive && _owners[existing].Binding.GrantGeneration > owner.GrantGeneration) return false;
            if (existing >= 0 && (pages.Count == 0 || !ReferenceEquals(_owners[existing].Binding, owner)))
            {
                // a new runtime of the same add-on takes over the entry (and its place in the order)
                var old = _owners[existing];
                if (pages.Count == 0)
                {
                    Drop(old);
                }
                else
                {
                    _ = old.Watch.Unregister();
                    _owners[existing] = new Owned(owner, [.. pages], Watch(owner));
                }
            }
            else if (existing >= 0)
            {
                _owners[existing] = _owners[existing] with { Pages = [.. pages] };
            }
            else if (pages.Count > 0)
            {
                _owners.Add(new Owned(owner, [.. pages], Watch(owner)));
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Drops the pages the binding owns (its runtime ended).</summary>
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

    // the runtime ending (disable, revoke, uninstall, sign-out) takes its pages away
    private CancellationTokenRegistration Watch(PluginRuntimeBinding owner) => owner.Lifetime.Register(() => Remove(owner));

    private void Drop(Owned entry)
    {
        _ = _owners.Remove(entry);
        _ = entry.Watch.Unregister();
    }

    private sealed record Owned(PluginRuntimeBinding Binding, IReadOnlyList<PluginPage> Pages, CancellationTokenRegistration Watch);
}
