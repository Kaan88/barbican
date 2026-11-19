// Mirrors: M1-plan §4.3 (ActiveApp, Style, Theme, MoreContrast, ReduceMotion, IsRapid); Rust main.rs:2093 (uiStyle), a11y.rs (A11y::from), motion.rs;
// Bulwark plugin-app-events.tsx (window, visibility, online, error hooks), stores/theme-store.ts (theme light|dark|system). Owner mailcore.

using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Core.Settings.Accessibility;
using Barbican.Core.Storage;
using Barbican.Presentation.Theme;
using Barbican.Presentation.Common;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Shell;

/// <summary>The shell state: which app, which look, contrast and motion, and the app lifecycle the plugins hear about.</summary>
public sealed class ShellViewModel : ObservableObject, IDisposable
{
    private readonly IPrefsStore _prefs;
    private readonly IDispatcher? _dispatcher;
    private readonly ISystemAccessibilitySource _system;
    private readonly MotionState _motion;
    private readonly IHookBus _hooks;
    private readonly TimeProvider _time;
    private AppId _activeApp = AppId.Mail;
    private IReadOnlyList<AppId> _available = [AppId.Mail];
    private ShellStyle _style = ShellStyle.Bulwark;
    private bool _dark = true;
    private bool _systemDark = true;
    private A11y _a11y = new();
    private string _windowTitle = "Barbican";
    private bool _sidebarOpen = true;
    private bool _sidebarCollapsed;

    public ShellViewModel(IPrefsStore prefs, ISystemAccessibilitySource system, MotionState motion, IHookBus hooks, TimeProvider time, IDispatcher? dispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(motion);
        ArgumentNullException.ThrowIfNull(hooks);
        ArgumentNullException.ThrowIfNull(time);
        _prefs = prefs;
        _dispatcher = dispatcher;
        _system = system;
        _motion = motion;
        _hooks = hooks;
        _time = time;
        _motion.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(MotionState.Rapid), StringComparison.Ordinal))
            {
                OnPropertyChanged(nameof(IsRapid));
            }
            else if (string.Equals(e.PropertyName, nameof(MotionState.Reduce), StringComparison.Ordinal))
            {
                OnPropertyChanged(nameof(ReduceMotion));
            }
        };
        _prefs.Changed += OnSettingsChanged;
        _system.Changed += OnSettingsChanged;
        Refresh();
    }

    public AppId ActiveApp => _activeApp;

    /// <summary>The apps that exist yet (M1: Mail); the others show on the rail but do not open.</summary>
    public IReadOnlyList<AppId> AvailableApps => _available;

    public ShellStyle Style => _style;

    public bool Dark => _dark;

    /// <summary>The eight-way combination the design tokens are keyed by.</summary>
    public ThemeVariant Variant => new(_dark, MoreContrast, _style == ShellStyle.Nordic, _style == ShellStyle.Bulwark);

    public bool MoreContrast => _a11y.MoreContrast;

    public bool ReduceMotion => _motion.Reduce;

    /// <summary>List feedback snaps while keys repeat (Motion.Rapid).</summary>
    public bool IsRapid => _motion.Rapid;

    /// <summary>The accessibility settings as the prefs and the OS give them.</summary>
    public A11y Accessibility => _a11y;

    public string WindowTitle
    {
        get => _windowTitle;
        private set => SetProperty(ref _windowTitle, value);
    }

    public bool SidebarOpen => _sidebarOpen;

    public bool SidebarCollapsed => _sidebarCollapsed;

    /// <summary>Which app is allowed to open (the apps that exist).</summary>
    public void SetAvailableApps(IReadOnlyList<AppId> apps)
    {
        ArgumentNullException.ThrowIfNull(apps);
        _available = apps;
        OnPropertyChanged(nameof(AvailableApps));
    }

    /// <summary>Opens an app of the rail; false when it does not exist yet.</summary>
    public bool TryActivate(AppId app)
    {
        if (!_available.Contains(app))
        {
            return false;
        }

        if (app == _activeApp)
        {
            return true;
        }

        var previous = _activeApp;
        _activeApp = app;
        OnPropertyChanged(nameof(ActiveApp));
        _hooks.Fire(HookNames.OnRouteLeave, () => new RoutePayload(Route(previous)));
        _hooks.Fire(HookNames.OnNavigate, () => new RoutePayload(Route(app)));
        _hooks.Fire(HookNames.OnRouteEnter, () => new RoutePayload(Route(app)));
        _hooks.Fire(HookNames.OnSidebarAppClose, () => new SidebarAppPayload(Route(previous)));
        _hooks.Fire(HookNames.OnSidebarAppOpen, () => new SidebarAppPayload(Route(app)));
        return true;
    }

    /// <summary>The OS is dark or light (the "system" theme follows it); the platform layer calls this.</summary>
    public void SetSystemDark(bool dark)
    {
        _systemDark = dark;
        Refresh();
    }

    /// <summary>The look for a settings preview or the automation <c>ui</c> command: in memory only, nothing is saved.</summary>
    public void PreviewLook(ShellStyle style, bool dark)
    {
        _style = style;
        _dark = dark;
        OnPropertyChanged(nameof(Style));
        OnPropertyChanged(nameof(Dark));
        OnPropertyChanged(nameof(Variant));
    }

    /// <summary>The window title with the unread badge (when the setting is on).</summary>
    public void SetUnreadBadge(long unread) =>
        WindowTitle = Core.Mail.Rules.MailRules.BadgeTitle(_prefs.Current.Bool("faviconUnreadBadge", true), unread);

    public void ToggleSidebar()
    {
        _sidebarOpen = !_sidebarOpen;
        OnPropertyChanged(nameof(SidebarOpen));
        var open = _sidebarOpen;
        _hooks.Fire(HookNames.OnSidebarToggle, () => new SidebarTogglePayload(open));
    }

    public void ToggleSidebarCollapsed()
    {
        _sidebarCollapsed = !_sidebarCollapsed;
        OnPropertyChanged(nameof(SidebarCollapsed));
        var collapsed = _sidebarCollapsed;
        _hooks.Fire(HookNames.OnSidebarCollapse, () => new SidebarCollapsePayload(collapsed));
    }

    /// <summary>The shell is up for an account.</summary>
    public void NotifyReady(string? accountId) => _hooks.Fire(HookNames.OnAppReady, () => new AppReadyPayload(accountId));

    /// <summary>The window got or lost focus; on activation the OS accessibility state is read again ("System" settings follow Windows).</summary>
    public void NotifyWindowActive(bool active)
    {
        if (active)
        {
            Refresh();
        }

        _hooks.Fire(active ? HookNames.OnWindowFocus : HookNames.OnWindowBlur, () => new EmptyPayload());
    }

    public void NotifyVisibility(bool visible) => _hooks.Fire(HookNames.OnVisibilityChange, () => new VisibilityPayload(visible));

    public void NotifyOnline(bool online) => _hooks.Fire(online ? HookNames.OnOnline : HookNames.OnOffline, () => new EmptyPayload());

    /// <summary>The window is closing (observers run without being waited for).</summary>
    public void NotifyBeforeClose() => _hooks.Fire(HookNames.OnBeforeUnload, () => new EmptyPayload());

    /// <summary>An unhandled error; the message is cut to 500 characters.</summary>
    public void ReportError(string message)
    {
        var text = (message ?? string.Empty).Length > 500 ? message![..500] : message ?? string.Empty;
        _hooks.Fire(HookNames.OnAppError, () => new AppErrorPayload(text));
    }

    /// <summary>Once a minute while the app is open (Bulwark PLUGIN_INTERVAL_MS).</summary>
    public void NotifyInterval() => _hooks.Fire(HookNames.OnInterval, () => new IntervalPayload(_time.GetUtcNow()));

    public void Dispose()
    {
        _prefs.Changed -= OnSettingsChanged;
        _system.Changed -= OnSettingsChanged;
    }

    private static string Route(AppId app) => app.ToString().ToLowerInvariant();

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (_dispatcher is not null && !_dispatcher.HasThreadAccess)
        {
            _dispatcher.Post(Refresh);
        }
        else
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        var prefs = _prefs.Current;
        var system = _system.Current;
        _a11y = A11y.From(prefs, system);
        _motion.Apply(prefs.Str("reduceMotion", "system"), system.ReduceMotion);
        _style = prefs.Str("uiStyle", "bulwark") switch { "nordic" => ShellStyle.Nordic, "bento" => ShellStyle.Bento, _ => ShellStyle.Bulwark };
        _dark = prefs.Str("theme", "system") switch { "dark" => true, "light" => false, _ => _systemDark };
        OnPropertyChanged(nameof(Accessibility));
        OnPropertyChanged(nameof(MoreContrast));
        OnPropertyChanged(nameof(Style));
        OnPropertyChanged(nameof(Dark));
        OnPropertyChanged(nameof(Variant));
    }
}
