// Mirrors: Rust cal_editor.rs:252 build_data return value. Contract from the M4 plan section 4.3; owner cal-edit.

using System.Text.Json.Nodes;

namespace Barbican.Core.Calendar.Editing;

/// <summary>The JSCalendar fields to write (a full object for a new event, a patch for an edit: unchanged fields are left out, a removed field is JSON null) and whether scheduling messages go out.</summary>
public sealed record EventData(JsonObject Data, bool SendSchedulingMessages);
