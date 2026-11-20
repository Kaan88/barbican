// Mirrors: Bulwark lib/plugin-sandbox/checkers.ts:26-48. No Stalwart counterpart.
using System.Collections.Immutable;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Core.Plugins.Api.Composer;

/// <summary>Only declarative metadata crosses the host boundary. Callbacks stay in the owning frame.</summary>
public sealed record PluginCheckerRegistration(PluginRuntimeBinding Owner, string Id, string Label,
    ImmutableArray<string> Kinds, TimeSpan Debounce, ImmutableArray<string> Languages, bool OnDemand, bool WarnBeforeSend);
