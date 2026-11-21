// Mirrors: Bulwark lib/proofreading/check-session.ts:37-47, segments.ts and annotations.ts:86-128. No Stalwart counterpart.
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Barbican.Core.Editor;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Composer;

/// <summary>One issued checker request, with a private map back to the editor's UTF-16 positions.</summary>
public sealed class PluginCheckRequest
{
    private const int MaxChars = 20_000;
    private const int MaxSegments = 512;
    private const char ObjectReplacement = '\uFFFC';
    private readonly Dictionary<string, Segment> _segments;

    private PluginCheckRequest(string alias, string language, Dictionary<string, Segment> segments, long version)
    {
        _segments = segments;
        Revision = Guid.NewGuid().ToString("N");
        DocVersion = version;
        Arguments = Build(alias, language, segments, wrap: true);
        Request = Build(alias, language, segments, wrap: false);
    }

    /// <summary>The revision a plugin must echo in setAnnotations.</summary>
    public string Revision { get; }

    /// <summary>The editor version this text came from; results for another version are dropped.</summary>
    public long DocVersion { get; }

    /// <summary>The request object itself, as composer.getCheckText returns it.</summary>
    public PluginValue Request { get; }

    private PluginValue Build(string alias, string language, Dictionary<string, Segment> segments, bool wrap)
    {
        var value = new PluginValueBuilder();
        var order = value.Array([.. segments.Keys.Select(value.String)]);
        var text = value.Array([.. segments.Select(pair =>
            value.Object(("id", value.String(pair.Key)), ("text", value.String(pair.Value.Text))))]);
        var request = value.Object(("composerId", value.String(alias)),
            ("revision", value.String(Revision)), ("language", value.String(language)),
            ("full", value.Boolean(true)), ("order", order), ("segments", text),
            ("objectReplacement", value.String(ObjectReplacement.ToString())));
        return value.Build(wrap ? value.Array(request) : request);
    }

    public PluginValue Arguments { get; }

    public static PluginCheckRequest? Create(Doc doc, string alias, string language, long version = 0)
    {
        if (!doc.HasComposerProvenance) return null;
        var segments = new Dictionary<string, Segment>(StringComparer.Ordinal);
        var chars = 0;
        for (var index = 0; index < doc.Blocks.Count; index++)
        {
            var block = doc.Blocks[index];
            if (block.Kind.IsAtomic || block.Kind is BlockKind.CodeBlock or BlockKind.Quote) continue;
            var chunk = new StringBuilder();
            var offset = 0;
            var start = 0;
            foreach (var run in block.Runs)
            {
                if (run.IsComposerContext)
                {
                    Add();
                    start = offset + run.Text.Length;
                }
                else
                {
                    if (run.Text.Length > MaxChars - chars - chunk.Length) return null;
                    chunk.Append(run.Text);
                }
                offset += run.Text.Length;
            }
            Add();
            if (segments.Count > MaxSegments) return null;

            void Add()
            {
                if (chunk.Length == 0) return;
                var text = chunk.ToString();
                chunk.Clear();
                if (string.IsNullOrWhiteSpace(text)) return;
                segments.Add(Guid.NewGuid().ToString("N"), new(new Pos(index, start), text));
                chars += text.Length;
            }
        }
        return segments.Count == 0 ? null : new(alias, language, segments, version);
    }

    public IReadOnlyList<PluginCheckIssue> Decode(PluginValue result, ImmutableArray<string> kinds)
    {
        if (result.TryGetProperty("status", out var status) &&
            status is { Kind: PluginValueKind.String, StringValue: "unavailable" or "paused" }) return [];
        var array = result.Kind == PluginValueKind.Array ? result : Field(result, "annotations");
        if (array?.Kind != PluginValueKind.Array) return [];
        var output = new List<PluginCheckIssue>();
        for (var index = 0; index < array.Count && output.Count < 300; index++)
        {
            if (DecodeItem(array.Item(index), kinds) is { } issue) output.Add(issue);
        }
        return output;
    }

    private PluginCheckIssue? DecodeItem(PluginValue item, ImmutableArray<string> kinds)
    {
        if (Field(item, "segmentId") is not { Kind: PluginValueKind.String, StringValue: { } id } || !_segments.TryGetValue(id, out var segment) ||
            Field(item, "kind") is not { Kind: PluginValueKind.String, StringValue: { } kind } || !kinds.Contains(kind, StringComparer.Ordinal) ||
            Field(item, "message") is not { Kind: PluginValueKind.String, StringValue.Length: > 0 } message ||
            !TryInt(Field(item, "offset"), out var offset) || offset < 0 || offset >= segment.Text.Length ||
            !TryInt(Field(item, "length"), out var length) || length < 1) return null;
        var covered = segment.Text.AsSpan(offset, Math.Min(length, segment.Text.Length - offset));
        if (char.IsLowSurrogate(covered[0]) || char.IsHighSurrogate(covered[^1]) ||
            covered.ContainsAny('\r', '\n', ObjectReplacement) || covered.IsWhiteSpace()) return null;
        var label = SafeLabel(message.StringValue);
        if (label.Length == 0) return null;
        var candidates = Field(item, "replacements");
        var replacements = ImmutableArray.CreateBuilder<string>();
        if (candidates?.Kind == PluginValueKind.Array)
        {
            for (var index = 0; index < candidates.Count && replacements.Count < 5; index++)
            {
                if (candidates.Item(index) is { Kind: PluginValueKind.String, StringValue: { Length: <= 100 } replacement } &&
                    !covered.SequenceEqual(replacement) && !replacements.Contains(replacement, StringComparer.Ordinal) &&
                    IsSafeText(replacement)) replacements.Add(replacement);
            }
        }
        return new(new Pos(segment.Start.B, segment.Start.O + offset), covered.ToString(), kind, label, replacements.ToImmutable());
    }

    internal static bool IsSafeText(string text)
    {
        var remaining = text.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out var rune, out var length) != System.Buffers.OperationStatus.Done ||
                !IsInsertable(rune)) return false;
            remaining = remaining[length..];
        }
        return true;
    }

    private static string SafeLabel(string message)
    {
        var label = new StringBuilder();
        foreach (var rune in message.EnumerateRunes())
        {
            if (label.Length + rune.Utf16SequenceLength > 500) break;
            if (IsInsertable(rune)) label.Append(rune.ToString());
        }
        return label.ToString().Trim();
    }

    private static bool IsInsertable(Rune value) => value.Value != ObjectReplacement && Rune.GetUnicodeCategory(value) is not
        (UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator);

    private static PluginValue? Field(PluginValue value, string name) =>
        value.TryGetProperty(name, out var field) ? field : null;

    private static bool TryInt(PluginValue? value, out int number) =>
        int.TryParse(value?.Kind == PluginValueKind.Number ? value.NumberText : null,
            NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number);

    private sealed record Segment(Pos Start, string Text);
}
