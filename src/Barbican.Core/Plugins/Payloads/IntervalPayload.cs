// Mirrors: Bulwark onInterval (plugin-app-events.tsx:79). Owner mailcore.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>Once a minute while the app is open.</summary>
public sealed record IntervalPayload(DateTimeOffset Now);
