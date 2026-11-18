// Mirrors: ISettingsNavigation. A real implementation (not a stub): the seam both UI streams share. M6 plan section 3.2.

namespace Barbican.Presentation.Settings;

/// <summary>The default <see cref="ISettingsNavigation"/>: a history of (page, anchor).</summary>
public sealed class SettingsNavigation : ISettingsNavigation
{
    private readonly List<(string Page, string? Anchor)> _history = [(SettingsPageIds.Account, null)];
    private int _at;

    public string Current => _history[_at].Page;

    public string? Anchor => _history[_at].Anchor;

    public bool CanGoBack => _at > 0;

    public bool CanGoForward => _at < _history.Count - 1;

    public event EventHandler? Changed;

    public void Open(string pageId, string? anchor = null)
    {
        if (SettingsNav.Find(pageId) is null)
        {
            return;
        }

        if (string.Equals(pageId, Current, StringComparison.Ordinal) && string.Equals(anchor, Anchor, StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(pageId, Current, StringComparison.Ordinal))
        {
            _history[_at] = (pageId, anchor);
        }
        else
        {
            _history.RemoveRange(_at + 1, _history.Count - _at - 1);
            _history.Add((pageId, anchor));
            _at++;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Back() => Move(-1);

    public bool Forward() => Move(1);

    private bool Move(int by)
    {
        var to = _at + by;
        if (to < 0 || to >= _history.Count)
        {
            return false;
        }

        _at = to;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
