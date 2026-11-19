using Barbican.Core.Common;

namespace Barbican.Core.Mail.Archive;

/// <summary>Reorganizes messages directly in an account's Archive root into dated subfolders.</summary>
public interface IArchiveReorganizer
{
    Task<Result<ArchiveReorganizationReport>> ReorganizeAsync(string accountKey, string mode, CancellationToken ct);
}

