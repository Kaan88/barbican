// Mirrors: Bulwark lib/plugin-hook-catalog.ts:165-:207 (the calendar and task hook payloads). Owner cal-edit; the hook names are in spec/plugins/hooks.csv.

namespace Barbican.Core.Calendar.Editing;

/// <summary>onICalSubscriptionChange: <c>Change</c> is added, updated, removed or refreshed.</summary>
public sealed record SubscriptionChangePayload(string SubscriptionId, string Change);
