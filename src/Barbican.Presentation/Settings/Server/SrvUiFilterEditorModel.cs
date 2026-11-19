// Mirrors: Bulwark components/filters/filter-rule-modal.tsx (name, match all or any, conditions, actions, "Don't apply later rules", spam opt-in, active period, reject only when advertised, regex only where the server runs it), Rust filters_x.rs rule form. I-432..I-434. Owner srv-ui.
// The form is built from FilterCond and FilterAct (Core records); the rule JSON comes from SrvUiFilterBridge.Build (FilterRules.BuildRule). Save runs on the UI context (ConfigureAwait(true)) so Finished and Changed fire on the UI thread.

using System.Text.Json.Nodes;
using Barbican.Core.Filters.Rules;

namespace Barbican.Presentation.Settings.Server;

/// <summary>The state of the rule editor dialog.</summary>
public sealed class SrvUiFilterEditorModel
{
    private readonly ISrvUiFiltersViewModel _page;
    private readonly JsonNode? _base;
    private readonly string _account;
    private readonly string? _state;
    private readonly long _accountGeneration;
    private readonly List<FilterCond> _conds = [];
    private readonly List<FilterAct> _acts = [];

    public SrvUiFilterEditorModel(ISrvUiFiltersViewModel page, JsonNode? existing)
    {
        ArgumentNullException.ThrowIfNull(page);
        _page = page;
        _base = existing;
        _account = page.StateAccountKey ?? string.Empty;
        _state = page.State?.State;
        _accountGeneration = page.AccountGeneration;
        Heading = existing is null ? "New rule" : "Edit rule";
        Folders = page.Folders;
        Extensions = page.State?.Extensions ?? [];
        Actions = [.. SrvUiFilterBridge.ActionKinds(Extensions, page.State?.RejectAdvertised ?? false)];
        if (existing is null)
        {
            Conditions.Add(new FilterCond("from", "contains", string.Empty, string.Empty));
            Acts.Add(new FilterAct("move", string.Empty, string.Empty, false));
            return;
        }

        Name = SrvUiFilterBridge.Str(existing, "name");
        Any = string.Equals(SrvUiFilterBridge.Str(existing, "matchType"), "any", StringComparison.Ordinal);
        Stop = existing["stopProcessing"]?.GetValue<bool>() ?? false;
        IncludeSpam = existing["includeSpam"]?.GetValue<bool>() ?? false;
        From = Day(SrvUiFilterBridge.Str(existing, "activeFrom"));
        Until = Day(SrvUiFilterBridge.Str(existing, "activeUntil"));
        _conds.AddRange(SrvUiFilterBridge.Conditions(existing));
        _acts.AddRange(SrvUiFilterBridge.Actions(existing));
        if (Conditions.Count == 0)
        {
            Conditions.Add(new FilterCond("from", "contains", string.Empty, string.Empty));
        }

        if (Acts.Count == 0)
        {
            Acts.Add(new FilterAct("keep", string.Empty, string.Empty, false));
        }
    }

    /// <summary>Something in the form changed shape (a row added or removed, a field changed); the view redraws.</summary>
    public event EventHandler? Changed;

    /// <summary>The dialog ends: saved when <see cref="Saved"/> is not null, cancelled otherwise.</summary>
    public event EventHandler? Finished;

    public string Heading { get; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Match any condition (else all).</summary>
    public bool Any { get; set; }

    /// <summary>"Don't apply later rules".</summary>
    public bool Stop { get; set; }

    /// <summary>Also move or copy mail the server marked as spam.</summary>
    public bool IncludeSpam { get; set; }

    /// <summary>The first day the rule acts (yyyy-MM-dd) or empty.</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>The last day the rule acts (yyyy-MM-dd) or empty.</summary>
    public string Until { get; set; } = string.Empty;

    public IList<FilterCond> Conditions => _conds;

    public IList<FilterAct> Acts => _acts;

    public IReadOnlyList<FilterFolder> Folders { get; }

    public IReadOnlyList<string> Extensions { get; }

    /// <summary>The action kinds offered (id, label).</summary>
    public IReadOnlyList<(string Id, string Label)> Actions { get; }

    /// <summary>The server can confine a rule to a period (Sieve date and relational), or the rule already has one.</summary>
    public bool PeriodsSupported => (Extensions.Contains("date", StringComparer.Ordinal) && Extensions.Contains("relational", StringComparer.Ordinal)) || From.Length > 0 || Until.Length > 0;

    /// <summary>A move or copy action is present, so the spam opt-in applies.</summary>
    public bool HasFolderAction => Acts.Any(a => a.Kind is "move" or "copy");

    public string? Error { get; private set; }

    public bool Saving { get; private set; }

    /// <summary>The comparators the condition offers: regex only where the server runs it or the row already uses it.</summary>
    public IReadOnlyList<string> ComparatorsFor(int index)
    {
        var c = Conditions[index];
        var regex = Extensions.Contains("regex", StringComparer.Ordinal);
        return [.. SrvUiFilterFormRules.Comparators(c.Field).Where(x => regex || !string.Equals(x, "regex", StringComparison.Ordinal) || string.Equals(c.Comparator, "regex", StringComparison.Ordinal))];
    }

    public void SetField(int index, string field)
    {
        Conditions[index] = SrvUiFilterFormRules.FieldChanged(Conditions[index], field);
        Raise();
    }

    public void SetComparator(int index, string comparator)
    {
        var c = Conditions[index];
        var cleared = SrvUiFilterFormRules.ValueKind(c.Field, c.Comparator) != SrvUiFilterFormRules.ValueKind(c.Field, comparator);
        Conditions[index] = c with { Comparator = comparator, Value = cleared ? string.Empty : c.Value };
        Raise();
    }

    public void SetValue(int index, string value) => Conditions[index] = Conditions[index] with { Value = value };

    public void SetHeader(int index, string header) => Conditions[index] = Conditions[index] with { Header = header };

    public void AddCondition()
    {
        Conditions.Add(new FilterCond("from", "contains", string.Empty, string.Empty));
        Raise();
    }

    public void RemoveCondition(int index)
    {
        if (Conditions.Count > 1)
        {
            Conditions.RemoveAt(index);
            Raise();
        }
    }

    public void SetActionKind(int index, string kind)
    {
        Acts[index] = new FilterAct(kind, string.Empty, string.Empty, false);
        Raise();
    }

    public void SetActionValue(int index, string value) => Acts[index] = Acts[index] with { Value = value };

    public void SetActionFolder(int index, string mailboxId)
    {
        var path = Folders.FirstOrDefault(f => string.Equals(f.Id, mailboxId, StringComparison.Ordinal))?.Path ?? string.Empty;
        Acts[index] = Acts[index] with { Mailbox = mailboxId, Value = path };
    }

    public void SetKeepCopy(int index, bool keep) => Acts[index] = Acts[index] with { KeepCopy = keep };

    public void AddAction()
    {
        Acts.Add(new FilterAct("mark_read", string.Empty, string.Empty, false));
        Raise();
    }

    public void RemoveAction(int index)
    {
        if (Acts.Count > 1)
        {
            Acts.RemoveAt(index);
            Raise();
        }
    }

    /// <summary>Validates, saves and closes on success; the error stays in the dialog otherwise (T-093, T-094, T-100, T-101).</summary>
    public async Task SaveAsync(CancellationToken ct)
    {
        Saving = true;
        Error = null;
        Raise();
        var built = SrvUiFilterBridge.Build(_base, null, Name, Any, _conds, _acts, Folders, Stop, HasFolderAction && IncludeSpam, From.Trim(), Until.Trim());
        if (built.Rule is null)
        {
            Error = built.Error ?? "The rule is not valid.";
            Saving = false;
            Raise();
            return;
        }

        var saved = await _page.UpsertAsync(built.Rule, _account, _state, _accountGeneration, ct).ConfigureAwait(true);
        Saving = false;
        if (!saved)
        {
            Error = _page.SaveError ?? "The rule could not be saved.";
            Raise();
            return;
        }

        Saved = built.Rule;
        Finished?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The rule as it was saved (null until it was).</summary>
    public JsonNode? Saved { get; private set; }

    public void Cancel() => Finished?.Invoke(this, EventArgs.Empty);

    private static string Day(string moment) => moment.Length >= 10 ? moment[..10] : moment;

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
