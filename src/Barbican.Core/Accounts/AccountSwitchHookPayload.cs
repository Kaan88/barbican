// Mirrors: Bulwark lib/plugin-hook-catalog.ts:281 (onAccountSwitch: { accountId, previousAccountId }).

namespace Barbican.Core.Accounts;

/// <summary>The payload of onAccountSwitch.</summary>
public sealed record AccountSwitchHookPayload(string AccountId, string? PreviousAccountId);
