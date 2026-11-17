// Mirrors: Bulwark lib/plugin-hook-catalog.ts:276 (onLogin: { accountId, username }).

namespace Barbican.Core.Accounts;

/// <summary>The payload of onLogin.</summary>
public sealed record LoginHookPayload(string AccountId, string Username);
