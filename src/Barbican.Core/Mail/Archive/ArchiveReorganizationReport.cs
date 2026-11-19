namespace Barbican.Core.Mail.Archive;

/// <summary>Known outcomes; cancellation or a transport failure can leave the current request's outcome unknown.</summary>
public sealed record ArchiveReorganizationReport(int Moved, int Skipped, int Failed, bool Cancelled, string? Detail)
{
    /// <summary>Whether the complete pre-write scan finished.</summary>
    public bool SnapshotComplete { get; init; }

    /// <summary>Messages present in the completed snapshot.</summary>
    public int Total { get; init; }

    /// <summary>Messages not attempted after an interruption.</summary>
    public int Unattempted { get; init; }

    /// <summary>Messages in a request whose outcome could not be established.</summary>
    public int Unknown { get; init; }
}
