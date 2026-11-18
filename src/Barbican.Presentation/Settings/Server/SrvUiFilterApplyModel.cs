// Mirrors: Rust filters_x.rs run_rule :566 and apply_with :451, Bulwark lib/filters/retroactive.ts (scan the Inbox, count, confirm, apply with ifInState, the result text); T-097..T-099. A result that arrives after the dialog closed or after a later run started is dropped (T-098).
// Owner srv-ui.

using System.Text.Json.Nodes;
using Barbican.Core.Filters.Rules;

namespace Barbican.Presentation.Settings.Server;

/// <summary>The state of the "Apply to existing mail" dialog.</summary>
public sealed class SrvUiFilterApplyModel
{
    private readonly ISrvUiFiltersViewModel _page;
    private readonly JsonNode _rule;
    private FilterScan? _scan;
    private long _generation;
    private bool _open = true;

    public SrvUiFilterApplyModel(ISrvUiFiltersViewModel page, JsonNode rule)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(rule);
        _page = page;
        _rule = rule;
        RuleName = SrvUiFilterBridge.Str(rule, "name");
    }

    /// <summary>The state changed; the view redraws (raised on the UI context).</summary>
    public event EventHandler? Changed;

    /// <summary>The dialog asks to close.</summary>
    public event EventHandler? Closed;

    public string RuleName { get; }

    public SrvUiApplyPhase Phase { get; private set; } = SrvUiApplyPhase.Scanning;

    public string Text { get; private set; } = "Looking through the Inbox...";

    public int Total => _scan?.Total ?? 0;

    /// <summary>Scans the Inbox (nothing is changed).</summary>
    public async Task StartAsync(CancellationToken ct)
    {
        if (SrvUiFilterBridge.RetroUnsupported(_rule) is { } why)
        {
            Set(SrvUiApplyPhase.Unsupported, why);
            return;
        }

        var run = ++_generation;
        Set(SrvUiApplyPhase.Scanning, "Looking through the Inbox...");
        var result = await _page.ScanAsync(_rule, run, ct).ConfigureAwait(true);
        if (!_open || run != _generation)
        {
            return;
        }

        if (!result.IsOk)
        {
            Set(SrvUiApplyPhase.Failed, result.Error!.Message);
            return;
        }

        _scan = result.Value;
        if (_scan!.Unsupported is { } unsupported)
        {
            Set(SrvUiApplyPhase.Unsupported, unsupported);
        }
        else if (_scan.Total == 0)
        {
            Set(SrvUiApplyPhase.Nothing, "No messages in the Inbox match this rule.");
        }
        else
        {
            Set(SrvUiApplyPhase.Ready, _scan.Total + " message" + (_scan.Total == 1 ? string.Empty : "s") + " in the Inbox match. Apply the rule to " + (_scan.Total == 1 ? "it" : "them") + "?");
        }
    }

    /// <summary>Applies the scanned plan after the user confirmed.</summary>
    public async Task ApplyAsync(CancellationToken ct)
    {
        if (_scan is null || Phase != SrvUiApplyPhase.Ready)
        {
            return;
        }

        Set(SrvUiApplyPhase.Applying, "Applying...");
        var result = await _page.ApplyAsync(_scan, ct).ConfigureAwait(true);
        if (!_open)
        {
            return;
        }

        if (!result.IsOk)
        {
            Set(SrvUiApplyPhase.Failed, result.Error!.Message);
            return;
        }

        var text = SrvUiFilterBridge.AppliedText(result.Value!, _scan.Total);
        Set(result.Value!.Kind == FilterAppliedKind.Done ? SrvUiApplyPhase.Done : SrvUiApplyPhase.Failed, text);
    }

    public void Close()
    {
        _open = false;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void Set(SrvUiApplyPhase phase, string text)
    {
        Phase = phase;
        Text = text;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
