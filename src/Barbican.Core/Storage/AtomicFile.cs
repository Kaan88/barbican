// Mirrors: PORT-PLAN.md §3.5 ("every write is atomic: write a temp file, then replace"); Rust settings.rs:136 save (a plain fs::write, which
// a crash could leave half-written). The temp file sits next to the target, so the final move is a rename on one volume.

using System.Text;

namespace Barbican.Core.Storage;

/// <summary>Whole-file reads and atomic whole-file writes (UTF-8, no BOM).</summary>
public static class AtomicFile
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Writes <paramref name="text"/> to <paramref name="path"/>: a temp file in the same folder, flushed, then moved over the target.</summary>
    public static void WriteAllText(string path, string text)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(text);
        IoThreadCheck.AssertNotUiThread();
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.None))
            {
                stream.Write(Utf8.GetBytes(text));
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    /// <summary>The same, off the calling thread.</summary>
    public static Task WriteAllTextAsync(string path, string text, CancellationToken ct) =>
        Task.Run(() => WriteAllText(path, text), ct);

    /// <summary>The file's text, or null when it does not exist or cannot be read.</summary>
    public static string? ReadAllTextOrNull(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        IoThreadCheck.AssertNotUiThread();
        try
        {
            return File.Exists(path) ? File.ReadAllText(path, Utf8) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Deletes the file when it exists.</summary>
    public static void Delete(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        IoThreadCheck.AssertNotUiThread();
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
