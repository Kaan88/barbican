// Mirrors: Bulwark lib/plugin-hook-catalog.ts lib/plugin-hook-catalog.ts:326 (payload of onColumnResize (column: sidebar | email-list | email-list-height)); spec/plugins/hooks.csv. Owner actions.

namespace Barbican.Core.Mail.Write.Payloads;

/// <summary>The payload of onColumnResize (column: sidebar | email-list | email-list-height).</summary>
public sealed record ColumnResizePayload(string Column, double Width);
