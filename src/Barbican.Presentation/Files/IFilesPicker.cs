// File and folder pickers for uploads (PORT-PLAN 4.4: a hidden run answers from a queue and never opens an OS dialog). Owner files-ui.

namespace Barbican.Presentation.Files;

/// <summary>Chooses local files or a local folder to upload.</summary>
public interface IFilesPicker
{
    /// <summary>Local file paths (null: cancelled).</summary>
    Task<IReadOnlyList<string>?> PickFilesAsync(CancellationToken ct);

    /// <summary>A local folder path (null: cancelled).</summary>
    Task<string?> PickFolderAsync(CancellationToken ct);
}
