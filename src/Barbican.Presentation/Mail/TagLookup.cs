// Mirrors: Bulwark hooks/use-keyword-format.ts:37 tagName, :57 tagColor, :76 sortTagIds. Owner mailcore.

namespace Barbican.Presentation.Mail;

/// <summary>What the tag list says about a tag id: its name, its colour and its place in the list.</summary>
public sealed record TagLookup
{
    /// <summary>No tag list: every tag is named by its id, grey, and ranks last.</summary>
    public static TagLookup None { get; } = new();

    public Func<string, string> Name { get; init; } = id => id;

    public Func<string, uint> Color { get; init; } = _ => 0x9ca3af;

    public Func<string, int> Rank { get; init; } = _ => int.MaxValue;
}
