// Mirrors: Bulwark lib/plugin-hook-catalog.ts:277-:279 (onAuthReady, onTokenRefresh, onAccountAdd: { accountId }).

namespace Barbican.Core.Accounts;

/// <summary>The payload of the account hooks that name one account.</summary>
public sealed record AccountHookPayload(string AccountId);
