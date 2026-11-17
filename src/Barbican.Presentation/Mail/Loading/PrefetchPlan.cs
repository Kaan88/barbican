// Mirrors: Rust msgload.rs prefetch :320 and prefetch_top :351 (tag rust-final): two ahead in the direction of travel (three while moving
// one row at a time) and one behind, plus the open message's neighbours in its conversation; a list load warms its first two. Bulwark
// lib/email-prefetch.ts (prefetch the neighbours of the selection). Owner body.

namespace Barbican.Presentation.Mail.Loading;

/// <summary>Which messages to warm around the open one (pure).</summary>
public static class PrefetchPlan
{
    /// <summary>How many messages a list load warms at its top.</summary>
    public const int Top = 2;

    /// <summary>The list indices to prefetch around <paramref name="index"/> of <paramref name="count"/> rows, and the travel state after it.</summary>
    public static (IReadOnlyList<int> Indices, TravelState Travel) Around(int index, int count, TravelState travel)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        // the same message again (opened, then shown): keep the direction it was reached in
        var (back, moving) = travel.Last switch
        {
            { } l when l == index => (travel.Back, travel.Moving),
            { } l => (l > index, Math.Abs(l - index) == 1),
            _ => (false, false),
        };
        var ahead = moving ? 3 : 2;
        var indices = new List<int>(ahead + 1);
        for (var k = 1; k <= ahead; k++)
        {
            indices.Add(back ? index - k : index + k);
        }

        indices.Add(back ? index + 1 : index - 1);
        return ([.. indices.Where(j => j >= 0 && j < count)], new TravelState(index, back, moving));
    }

    /// <summary>The conversation neighbours of the current strip item (before and after it).</summary>
    public static IReadOnlyList<string> ConversationNeighbours(IReadOnlyList<ConversationItem> strip)
    {
        ArgumentNullException.ThrowIfNull(strip);
        var k = -1;
        for (var i = 0; i < strip.Count; i++)
        {
            if (strip[i].IsCurrent)
            {
                k = i;
                break;
            }
        }

        if (k < 0)
        {
            return [];
        }

        var ids = new List<string>(2);
        if (k > 0)
        {
            ids.Add(strip[k - 1].Id);
        }

        if (k + 1 < strip.Count)
        {
            ids.Add(strip[k + 1].Id);
        }

        return ids;
    }
}
