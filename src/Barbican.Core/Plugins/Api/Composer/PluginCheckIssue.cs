// Mirrors: Bulwark lib/proofreading/annotations.ts:33-48. No Stalwart counterpart.
using System.Collections.Immutable;
using Barbican.Core.Editor;

namespace Barbican.Core.Plugins.Api.Composer;

public sealed record PluginCheckIssue(Pos Start, string Text, string Kind, string Message, ImmutableArray<string> Replacements)
{
    public Pos End => new(Start.B, Start.O + Text.Length);
}
