#if AUTOMATION_WRITES || BARBICAN_FILTER_TESTS
using Barbican.Core.Filters.Rules;

namespace Barbican.App.MailBody.Automation;

/// <summary>Bound operations for one already-guarded account; errors never imply that a write did not happen.</summary>
public interface IFilterWriteBackend
{
    Task<FilterWriteSnapshot> CaptureAsync(CancellationToken ct);
    Task EnsureUniqueAsync(string token, CancellationToken ct);
    Task<string> ImportAsync(string token, CancellationToken ct);
    Task<string?> DiscoverOwnMessageAsync(string token, CancellationToken ct);
    Task<string> CreateFolderAsync(string token, CancellationToken ct);
    Task<string?> DiscoverOwnFolderAsync(string token, CancellationToken ct);
    Task<(string State, System.Text.Json.Nodes.JsonNode Rule)> SaveRuleAsync(FilterWriteSnapshot original, string token, string folderId, CancellationToken ct);
    Task<FilterScan> ScanAsync(System.Text.Json.Nodes.JsonNode rule, CancellationToken ct);
    Task ApplyAndVerifyAsync(FilterScan scan, string emailId, string folderId, CancellationToken ct);
    Task<bool> DestroyOwnMessageAsync(string token, string emailId, CancellationToken ct);
    Task<bool> DestroyOwnEmptyFolderAsync(string token, string folderId, CancellationToken ct);
    Task<bool> RestoreAsync(FilterWriteSnapshot original, string expectedState, CancellationToken ct);
}

#endif
