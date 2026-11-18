// Mirrors: Bulwark lib/plugin-hook-catalog.ts:260 (onSieveScriptGenerate: the script text and the generate context). Owner srv-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The generated Sieve script before it is uploaded; a transform handler returns it with the text changed (Bulwark passes the script and a SieveScriptGenerateContext; one record here because the bus carries one value).</summary>
public sealed record FilterScriptPayload(string Script, string AccountId, int RuleCount);
