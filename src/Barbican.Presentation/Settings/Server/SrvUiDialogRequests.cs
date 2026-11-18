// Mirrors: M2 plan section 4.2 (content records per dialog live with their owner); PORT-PLAN section 3.7. The three dialogs of the Filters page: the rule editor, apply to existing mail and the read-only Sieve view. Owner srv-ui.

#pragma warning disable MA0048 // three small records of one family live in one file
using Barbican.Presentation.Dialogs;

namespace Barbican.Presentation.Settings.Server;

/// <summary>The rule editor (add or edit); the answer is true when the rule was saved.</summary>
public sealed record SrvUiFilterEditorRequest(SrvUiFilterEditorModel Model) : DialogRequest<bool>(Model?.Heading ?? "Rule");

/// <summary>Apply a rule to the mail already in the Inbox.</summary>
public sealed record SrvUiFilterApplyRequest(SrvUiFilterApplyModel Model) : DialogRequest<bool>("Apply to existing mail");

/// <summary>The generated Sieve script, read-only.</summary>
public sealed record SrvUiSieveRequest(string Script) : DialogRequest<bool>("Sieve script");
