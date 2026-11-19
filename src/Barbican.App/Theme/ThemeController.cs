// Mirrors: PORT-PLAN.md §3.7 (theme, contrast and style are applied by rewriting the palette brushes in place: no re-templating, no flash); Bulwark
// stores/theme-store.ts:578-640 (the CSS goes to onThemeBeforeApply, the original is applied first so nothing waits) and :703-710 (onThemeChange);
// design/MOTION.md rule 4 (reduce motion). Reads uiStyle/theme/contrast through ShellViewModel, which reads IPrefsStore.

using Barbican.App.Motion;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Storage;
using Barbican.Presentation.Shell;
using Barbican.Presentation.Theme;

namespace Barbican.App.Theme;

/// <summary>
/// Keeps the look of the window in step with <see cref="ShellViewModel"/>: palette brushes rewritten in place, the shape dictionary and
/// <see cref="StyleState"/> swapped once per style change, the root's requested theme (so the stock controls follow), the motion scale, and the two theme hooks.
/// </summary>
public sealed class ThemeController : IDisposable
{
    private readonly ShellViewModel _shell;
    private readonly StyleState _style;
    private readonly IHookBus _hooks;
    private readonly IPrefsStore _prefs;
    private FrameworkElement? _root;
    private ThemeVariant? _override;
    private ThemeVariant? _applied;
    private string? _appliedSetting;

    public ThemeController(ShellViewModel shell, StyleState style, IHookBus hooks, IPrefsStore prefs)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(hooks);
        ArgumentNullException.ThrowIfNull(prefs);
        _shell = shell;
        _style = style;
        _hooks = hooks;
        _prefs = prefs;
        _shell.PropertyChanged += OnShellChanged;
    }

    /// <summary>Raised on the UI thread after a variant was applied, with the CSS custom-property block of its palette (the body shell may use it).</summary>
    public event EventHandler<ThemeCssEventArgs>? CssApplied;

    /// <summary>The variant on screen.</summary>
    public ThemeVariant? Applied => _applied;

    /// <summary>The element whose RequestedTheme follows the variant (the shell page's root).</summary>
    public void Attach(FrameworkElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        _root = root;
        MotionRuntime.Scale = new MotionScale(_shell.ReduceMotion, _shell.IsRapid);
        _style.SetAccessibility(_shell.Accessibility, _prefs.Current.Bool("showToolbarLabels", true));
        Apply(force: true);
    }

    /// <summary>A look that is not in the settings (the automation <c>ui</c> command, a settings preview): in memory only. Null returns to the settings.</summary>
    public void Preview(ThemeVariant? variant)
    {
        _override = variant;
        Apply(force: false);
    }

    public void Dispose() => _shell.PropertyChanged -= OnShellChanged;

    private void OnShellChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ShellViewModel.Accessibility):
                _style.SetAccessibility(_shell.Accessibility, _prefs.Current.Bool("showToolbarLabels", true));
                break;
            case nameof(ShellViewModel.ReduceMotion):
            case nameof(ShellViewModel.IsRapid):
                MotionRuntime.Scale = new MotionScale(_shell.ReduceMotion, _shell.IsRapid);
                break;
            case nameof(ShellViewModel.Variant):
            case nameof(ShellViewModel.Style):
            case nameof(ShellViewModel.Dark):
            case nameof(ShellViewModel.MoreContrast):
                if (_override is null)
                {
                    Apply(force: false);
                }

                break;
            default:
                break;
        }
    }

    private void Apply(bool force)
    {
        var variant = _override ?? _shell.Variant;
        var setting = _prefs.Current.Str("theme", "system");
        if (!force && _applied == variant && string.Equals(_appliedSetting, setting, StringComparison.Ordinal))
        {
            return;
        }

        var before = _applied;
        _applied = variant;

        // one UI turn: every brush, the shape and the requested theme change before the next frame
        var palette = Tokens.Palette(variant);
        PaletteApplier.Apply(variant);
        AppResources.UseShape(variant.Nordic);
        _style.SetStyle(variant.Nordic);
        if (_root is not null)
        {
            _root.RequestedTheme = variant.Dark ? ElementTheme.Dark : ElementTheme.Light;
        }

        var css = ThemeCss.For(palette);
        CssApplied?.Invoke(this, new ThemeCssEventArgs(css));
        // Bulwark stores/theme-store.ts:703-710: fired when the theme setting OR the resolved scheme changed (system -> dark while already dark counts), not for style or contrast.
        var resolved = variant.Dark ? "dark" : "light";
        if (before is { } previous && (previous.Dark != variant.Dark || !string.Equals(_appliedSetting, setting, StringComparison.Ordinal)))
        {
            _hooks.Fire(HookNames.OnThemeChange, () => new ThemeChangePayload(setting, resolved));
        }

        _appliedSetting = setting;

        if (_hooks.HasHandlers(HookNames.OnThemeBeforeApply))
        {
            _ = TransformAsync(css, variant);
        }
    }

    // Bulwark applies the original CSS first and re-applies a plugin's version when it arrives; a newer variant makes the old answer stale.
    private async Task TransformAsync(string css, ThemeVariant variant)
    {
        var shaped = await _hooks.ShapeAsync(HookNames.OnThemeBeforeApply, css, CancellationToken.None);
        var dispatcher = _root?.DispatcherQueue;
        if (dispatcher is not null && !string.Equals(shaped, css, StringComparison.Ordinal))
        {
            _ = dispatcher.TryEnqueue(() =>
            {
                if (_applied == variant)
                {
                    CssApplied?.Invoke(this, new ThemeCssEventArgs(shaped));
                }
            });
        }
    }
}
