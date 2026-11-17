// Mirrors: Rust msgload.rs prefetch :320 (u.get_mails(): the list rows in order) and prefetch_top :351. Owner body.

namespace Barbican.Presentation.Mail.Loading;

/// <summary>The messages of the open list, in list order, for prefetching around the open one.</summary>
public interface IMessageNeighbours
{
    IReadOnlyList<MessageRowViewModel> Rows { get; }

    /// <summary>A first page of the list arrived (the prefetch of its top messages).</summary>
    event EventHandler? ListLoaded;
}
