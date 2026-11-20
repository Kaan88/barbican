#if WINDOWS
// Mirrors: Bulwark components/email/proofreading/use-proofreading.ts and lib/proofreading/composer-proofreader.ts.
using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Barbican.App.Plugins;
using Barbican.App.Plugins.Sandbox;
using Barbican.App.Shell;
using Barbican.App.Views.Editor;
using Barbican.Core.Accounts;
using Barbican.Core.OrganizationPolicy;
using Barbican.Core.Plugins.Api.Composer;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Runtime;
using Barbican.Core.Storage;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Editor;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Compose;

[SuppressMessage("Design", "CA1001", Justification = "The view owns the checker token only while loaded and cancels and disposes it on unload.")]
public sealed partial class ComposerView
{
    private PluginCheckerRegistry? _checkers;
    private PluginBackgroundRuntimes? _checkerRuntimes;
    private IPrefsStore? _checkerPrefs;
    private IAccountOverview? _checkerAccounts;
    private OrganizationPolicyService? _checkerPolicy;
    private OrganizationPolicySnapshot? _checkerPolicySnapshot;
    private IEditorViewModel? _checkerEditor;
    private DispatcherQueueTimer? _checkerTimer;
    private DispatcherQueueTimer? _checkerPolicyTimer;
    private CancellationTokenSource? _checkerLifetime;
    private readonly Dictionary<PluginCheckerRegistration, string> _checkerAliases = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, Queue<long>> _checkerCalls = new(StringComparer.Ordinal);
    private long _checkerVersion;
    private bool _checkerBusy;
    private bool _checkerPending;
    private bool _checkerPolicyBusy;

    private void WatchProofreading()
    {
        if (_checkers is not null) return;
        _checkers = ViewHost.Get<PluginCheckerRegistry>();
        _checkerRuntimes = ViewHost.Get<PluginBackgroundRuntimes>();
        _checkerPrefs = ViewHost.Get<IPrefsStore>();
        _checkerAccounts = ViewHost.Find<IAccountOverview>();
        _checkerPolicy = ViewHost.Get<OrganizationPolicyService>();
        _checkerTimer = DispatcherQueue.CreateTimer();
        _checkerTimer.IsRepeating = false;
        _checkerTimer.Tick += OnProofreadingTick;
        _checkerPolicyTimer = DispatcherQueue.CreateTimer();
        _checkerPolicyTimer.Interval = TimeSpan.FromSeconds(25);
        _checkerPolicyTimer.IsRepeating = true;
        _checkerPolicyTimer.Tick += OnProofreadingPolicyTick;
        _checkerLifetime = new CancellationTokenSource();
        _checkers.Changed += OnProofreadingRuntimeChanged;
        _checkerRuntimes.RunningChanged += OnProofreadingRuntimeChanged;
        _checkerPrefs.Changed += OnProofreadingSettingsChanged;
        _checkerPolicy.ProofreadingPolicyChanged += OnProofreadingPolicyChanged;
        if (_checkerAccounts is not null) _checkerAccounts.StatusesChanged += OnProofreadingPolicyChanged;
        BindProofreading(_vm?.Editor);
    }

    private void UnwatchProofreading()
    {
        if (_checkers is null || _checkerRuntimes is null || _checkerPrefs is null || _checkerTimer is null ||
            _checkerPolicy is null || _checkerPolicyTimer is null) return;
        _checkers.Changed -= OnProofreadingRuntimeChanged;
        _checkerRuntimes.RunningChanged -= OnProofreadingRuntimeChanged;
        _checkerPrefs.Changed -= OnProofreadingSettingsChanged;
        _checkerPolicy.ProofreadingPolicyChanged -= OnProofreadingPolicyChanged;
        if (_checkerAccounts is not null) _checkerAccounts.StatusesChanged -= OnProofreadingPolicyChanged;
        _checkers = null;
        _checkerRuntimes = null;
        _checkerPrefs = null;
        _checkerAccounts = null;
        _checkerPolicy = null;
        _checkerPolicySnapshot = null;
        _checkerTimer.Stop();
        _checkerTimer.Tick -= OnProofreadingTick;
        _checkerTimer = null;
        _checkerPolicyTimer.Stop();
        _checkerPolicyTimer.Tick -= OnProofreadingPolicyTick;
        _checkerPolicyTimer = null;
        _checkerLifetime?.Cancel();
        _checkerLifetime?.Dispose();
        _checkerLifetime = null;
        BindProofreading(null);
    }

    private void BindProofreading(IEditorViewModel? editor)
    {
        if (!ReferenceEquals(editor, _checkerEditor))
        {
            if (_checkerEditor is { } previous)
            {
                previous.Session.Changed -= OnProofreadingTextChanged;
                previous.ToolbarChanged -= OnProofreadingTextChanged;
            }
            _checkerAliases.Clear();
            _checkerPolicySnapshot = null;
            _checkerEditor = editor;
            if (editor is not null)
            {
                editor.Session.Changed += OnProofreadingTextChanged;
                editor.ToolbarChanged += OnProofreadingTextChanged;
            }
        }
        StartProofreadingPolicyRefresh();
        ScheduleProofreading();
    }

    private void OnProofreadingTextChanged(object? sender, EventArgs args) => ScheduleProofreading();

    private void OnProofreadingSettingsChanged(object? sender, EventArgs args) =>
        _ = DispatcherQueue.TryEnqueue(ScheduleProofreading);

    private void OnProofreadingRuntimeChanged(object? sender, EventArgs args) =>
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            StartProofreadingPolicyRefresh();
            ScheduleProofreading();
        });

    private void OnProofreadingPolicyChanged(object? sender, EventArgs args) =>
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_checkerPolicySnapshot?.TryRunWhileProofreadingCurrent(static () => true) != true &&
                _checkerPolicyTimer is { IsRunning: true } timer)
            {
                _checkerPolicySnapshot = null;
                RenderProofreading([], "Proofreading off");
                OnProofreadingPolicyTick(timer, EventArgs.Empty);
            }
        });

    private void StartProofreadingPolicyRefresh()
    {
        if (_checkerPolicyTimer is not { } timer) return;
        if (_checkerEditor is null || AvailableCheckers().Length == 0) { timer.Stop(); return; }
        if (!timer.IsRunning)
        {
            timer.Start();
            OnProofreadingPolicyTick(timer, EventArgs.Empty);
        }
    }

    private void OnProofreadingPolicyTick(DispatcherQueueTimer sender, object args)
    {
        if (!_checkerPolicyBusy && _checkerLifetime is { } lifetime) _ = RefreshProofreadingPolicyAsync(lifetime.Token);
    }

    private async Task RefreshProofreadingPolicyAsync(CancellationToken ct)
    {
        if (_checkerPolicy is not { } policy || _vm is not { } draft || _checkerEditor is null) return;
        _checkerPolicyBusy = true;
        try
        {
            var read = await policy.ReadAsync(draft.AccountKey, ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested || !ReferenceEquals(_vm, draft)) return;
            var previousCurrent = _checkerPolicySnapshot?.TryRunWhileProofreadingCurrent(static () => true) == true;
            _checkerPolicySnapshot = read.Status == OrganizationPolicyTransportStatus.Authoritative ? read.Snapshot : null;
            if (!previousCurrent) ScheduleProofreading();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { _checkerPolicyBusy = false; }
    }

    private PluginCheckerRegistration[] AvailableCheckers() =>
        _vm is { } draft && _checkers is { } registry && _checkerRuntimes is { } runtimes
            ? [.. registry.Current().Where(spec => !spec.OnDemand &&
                runtimes.ActiveRuntime(spec.Owner, draft.AccountKey, PluginCheckerRegistry.TypingPermission) is IPluginFencedHookRuntime)]
            : [];

    private void ScheduleProofreading()
    {
        _checkerVersion++;
        _checkerPending = false;
        RenderProofreading([], "Proofreading");
        if (_checkerTimer is null) return;
        _checkerTimer.Stop();
        var available = AvailableCheckers();
        foreach (var old in _checkerAliases.Keys.Except<PluginCheckerRegistration>(available, ReferenceEqualityComparer.Instance).ToArray())
            _checkerAliases.Remove(old);
        if (_checkerEditor is not { SourceOpen: false } || available.Length == 0) return;
        _checkerPending = true;
        // The first check waits for composer options and registry state to settle.
        _checkerTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(_checkerAliases.Count == 0 ? 1000 : 300,
            available.Max(spec => spec.Debounce.TotalMilliseconds)));
        _checkerTimer.Start();
    }

    private void OnProofreadingTick(DispatcherQueueTimer sender, object args)
    {
        if (!_checkerBusy && _checkerPending && _checkerLifetime is { } lifetime)
            _ = RunProofreadingAsync(lifetime.Token);
    }

    private async Task RunProofreadingAsync(CancellationToken ct)
    {
        _checkerBusy = true;
        _checkerPending = false;
        var version = _checkerVersion;
        try
        {
            if (_vm is not { } draft || _checkerEditor is not { SourceOpen: false } editor ||
                _checkers is not { } registry || _checkerRuntimes is not { } runtimes || _checkerPrefs is not { } prefs) return;
            var checkers = AvailableCheckers();
            var owner = ViewHost.Get<IPluginSessionSource>().CaptureOwner(draft.AccountKey).Value;
            if (checkers.Length == 0 || owner is null || !string.Equals(owner.LocalAccountKey, draft.AccountKey, StringComparison.Ordinal)) return;

            if (_checkerPolicySnapshot is not { } snapshot || !snapshot.TryRunWhileProofreadingCurrent(static () => true))
            {
                RenderProofreading([], "Proofreading off");
                return;
            }

            var language = ProofreadingLanguage(prefs.Current);
            var issues = new List<ProofreadingChoice>();
            var allowed = checkers.Where(spec => Allows(prefs.Current, snapshot, spec) && AcceptsLanguage(spec, language)).ToArray();
            foreach (var spec in allowed)
            {
                if (!IsCurrent()) return;
                if (runtimes.ActiveRuntime(spec.Owner, draft.AccountKey, PluginCheckerRegistry.TypingPermission) is not IPluginFencedHookRuntime fenced)
                    continue;
                if (PluginCheckRequest.Create(editor.Session.Doc, CheckerAlias(spec), language) is not { } request ||
                    !TryBudget(spec.Owner.Package.Id)) continue;

                bool UnderPolicy(Func<bool> issue) => snapshot.TryRunWhileProofreadingCurrent(() =>
                    prefs.TryRunWhileCurrent(settings => IsCurrent() && Allows(settings, snapshot, spec) && issue()));
                var reply = await fenced.InvokeCheckerAsync(spec, registry, draft.AccountKey, request.Arguments, UnderPolicy, ct).ConfigureAwait(true);
                if (!IsCurrent()) return;
                if (!reply.IsOk || reply.Value is not { } result || !registry.Contains(spec) || !UnderPolicy(static () => true)) continue;
                CollectProofreadingChoices(issues, request.Decode(result, spec.Kinds), spec,
                    (issue, replacement) => ApplyProofreadingAsync(spec, issue, replacement, draft, editor, registry, UnderPolicy, ct));
            }
            if (!IsCurrent()) return;
            if (await PublishProofreadingAsync(issues, draft.AccountKey, snapshot, prefs, registry, IsCurrent, ct).ConfigureAwait(true)) return;
            if (IsCurrent()) RenderProofreading([], allowed.Length > 0 ? "Proofreading" : "Proofreading off");

            bool IsCurrent() => !ct.IsCancellationRequested && version == _checkerVersion &&
                ReferenceEquals(editor, _checkerEditor) && !editor.SourceOpen && IsActionDraftCurrent(draft) &&
                ViewHost.Get<IPluginSessionSource>().IsCurrent(owner);

        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            _checkerBusy = false;
            if (_checkerPending && _checkerTimer is { IsRunning: false } timer) timer.Start();
        }
    }

    private static string ProofreadingLanguage(PrefsSnapshot prefs)
    {
        var language = prefs.Str("writingLanguage", "auto");
        return string.Equals(language, "auto", StringComparison.Ordinal)
            ? CultureInfo.CurrentCulture.Name is { Length: > 0 } configured ? configured : "en"
            : language;
    }

    private string CheckerAlias(PluginCheckerRegistration spec)
    {
        if (!_checkerAliases.TryGetValue(spec, out var alias))
            _checkerAliases.Add(spec, alias = "chk-" + Guid.NewGuid().ToString("N"));
        return alias;
    }

    private static void CollectProofreadingChoices(List<ProofreadingChoice> choices, IReadOnlyList<PluginCheckIssue> results,
        PluginCheckerRegistration spec, Func<PluginCheckIssue, string, Task> apply)
    {
        foreach (var issue in results)
        {
            if (choices.Count >= 300) return;
            if (choices.Exists(choice => issue.Start < choice.Issue.End && choice.Issue.Start < issue.End)) continue;
            choices.Add(new(issue, spec, replacement => apply(issue, replacement)));
        }
    }

    private Task<bool> PublishProofreadingAsync(IReadOnlyList<ProofreadingChoice> issues, string account,
        OrganizationPolicySnapshot snapshot, IPrefsStore prefs, PluginCheckerRegistry registry,
        Func<bool> isCurrent, CancellationToken ct)
    {
        var bindings = issues.Select(choice => choice.Spec.Owner).Distinct().ToArray();
        return bindings.Length > 0
            ? ViewHost.Get<PluginPackageHost>().TryPublishProofreadingAsync(bindings, account,
                () => snapshot.TryRunWhileProofreadingCurrent(() => prefs.TryRunWhileCurrent(settings =>
                    isCurrent() && PluginRuntimeBinding.TryWriteWhileAllAlive(bindings, () =>
                    {
                        var current = issues.Where(choice => registry.Contains(choice.Spec) &&
                            Allows(settings, snapshot, choice.Spec)).ToArray();
                        RenderProofreading(current, current.Length switch { 0 => "Proofreading off",
                            1 => "1 writing issue", _ => $"{current.Length} writing issues" });
                        return true;
                    }))), ct)
            : Task.FromResult(false);
    }

    private static async Task ApplyProofreadingAsync(PluginCheckerRegistration spec, PluginCheckIssue issue, string replacement,
        ComposerViewModel draft, IEditorViewModel editor, PluginCheckerRegistry registry, Func<Func<bool>, bool> underPolicy, CancellationToken ct)
    {
        try
        {
            _ = await ViewHost.Get<PluginPackageHost>().TryPostProofreadingAsync(spec.Owner, draft.AccountKey, () =>
                underPolicy(() => PluginRuntimeBinding.TryWriteWhileAllAlive([spec.Owner], () => registry.TryRunWhileRegistered(spec, () =>
                {
                    if (!editor.Session.Doc.TextRange(issue.Start, issue.End).AsSpan().SequenceEqual(issue.Text)) return false;
                    editor.Session.ReplaceRange(issue.Start, issue.End, replacement);
                    return true;
                }))), ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private static bool Allows(PrefsSnapshot prefs, OrganizationPolicySnapshot snapshot, PluginCheckerRegistration spec) =>
        snapshot.Effective.Proofreading.Allows(spec.Owner.Package.Id,
            ReadChoice(prefs.Values["proofreading"]),
            ReadChoice((prefs.Values["proofreadingPlugins"] as JsonObject)?[spec.Owner.Package.Id]));

    private static bool? ReadChoice(JsonNode? node) => node is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : null;

    private static bool AcceptsLanguage(PluginCheckerRegistration spec, string language) =>
        spec.Languages.IsEmpty || spec.Languages.Any(tag => string.Equals(tag, language, StringComparison.OrdinalIgnoreCase) ||
            language.StartsWith(tag + "-", StringComparison.OrdinalIgnoreCase));

    private bool TryBudget(string pluginId)
    {
        var time = ViewHost.Get<TimeProvider>();
        var now = time.GetTimestamp();
        foreach (var entry in _checkerCalls.ToArray())
        {
            while (entry.Value.TryPeek(out var first) && time.GetElapsedTime(first, now) >= TimeSpan.FromMinutes(1))
                entry.Value.Dequeue();
            if (entry.Value.Count == 0) _checkerCalls.Remove(entry.Key);
        }
        if (!_checkerCalls.TryGetValue(pluginId, out var calls))
        {
            if (_checkerCalls.Count >= 128) return false;
            _checkerCalls.Add(pluginId, calls = new());
        }
        if (calls.Count >= 30) return false;
        calls.Enqueue(now);
        return true;
    }

    private void RenderProofreading(IReadOnlyList<ProofreadingChoice> issues, string status)
    {
        _vm?.SetProofreadingWarningCount(issues.Count(choice => choice.Spec.WarnBeforeSend));
        if (BodyHost.Content is EditorView editor)
        {
            var plain = _checkerEditor?.Session.Plain == true;
            editor.ShowProofreading(plain ? [] : [.. issues.Select(choice => choice.Issue)],
                !plain && issues.Any(choice => choice.Spec.Kinds.Contains("spelling", StringComparer.Ordinal)));
        }
        ProofreadingMenu.Hide();
        ProofreadingMenu.Items.Clear();
        ProofreadingButton.Visibility = _checkerEditor is not null && AvailableCheckers().Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ProofreadingButton.IsEnabled = issues.Count > 0;
        ProofreadingButton.Content = status;
        AutomationProperties.SetName(ProofreadingButton, status);
        for (var index = 0; index < Math.Min(25, issues.Count); index++)
        {
            var choice = issues[index];
            var id = string.Create(CultureInfo.InvariantCulture, $"composer.proofreading.issue.{index}");
            var item = new MenuFlyoutSubItem { Text = choice.Issue.Text };
            AutomationProperties.SetAutomationId(item, id);
            item.Items.Add(new MenuFlyoutItem { Text = choice.Issue.Message, IsEnabled = false });
            item.Items.Add(new MenuFlyoutItem { Text = choice.Spec.Label, IsEnabled = false });
            for (var suggestionIndex = 0; suggestionIndex < choice.Issue.Replacements.Length; suggestionIndex++)
            {
                var replacement = choice.Issue.Replacements[suggestionIndex];
                var suggestion = new MenuFlyoutItem { Text = replacement.Length > 0 ? replacement : "Remove" };
                AutomationProperties.SetAutomationId(suggestion, string.Create(CultureInfo.InvariantCulture, $"{id}.replace.{suggestionIndex}"));
                suggestion.Click += (_, _) => _ = choice.Apply(replacement);
                item.Items.Add(suggestion);
            }
            ProofreadingMenu.Items.Add(item);
        }
    }

    private sealed record ProofreadingChoice(PluginCheckIssue Issue, PluginCheckerRegistration Spec, Func<string, Task> Apply);
}
#endif
