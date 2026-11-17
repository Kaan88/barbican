// Mirrors: Rust reader.rs:803 save_eml, :824 download_all (the file lands in Downloads under a unique name); M2 plan §6.3 hidden-automation scenario
// ("queued save answers ... into the run's temp folder"). Where IAttachmentSaver writes: a queued answer first (automation), else the user's Downloads
// folder with " (n)" added on a clash. A save dialog is an open item. Owner reader.

using System.Collections.Concurrent;

namespace Barbican.App.MailBody;

/// <summary>Answers "where to save" for the attachment saver.</summary>
public sealed class SaveTargets
{
    private readonly ConcurrentQueue<string> _queued = new();

    /// <summary>The last file written (automation reads it back).</summary>
    public string? LastPath { get; private set; }

    /// <summary>The next save goes to <paramref name="path"/> (hidden automation runs).</summary>
    public void Queue(string path) => _queued.Enqueue(path);

    public Task<Stream?> PickAsync(string suggested, CancellationToken ct)
    {
        string path;
        if (_queued.TryDequeue(out var queued))
        {
            path = queued;
        }
        else
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Directory.CreateDirectory(folder);
            path = Unique(folder, string.IsNullOrWhiteSpace(suggested) ? "attachment" : suggested);
        }

        LastPath = path;
        return Task.FromResult<Stream?>(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true));
    }

    private static string Unique(string folder, string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        var path = Path.Combine(folder, name);
        for (var n = 1; File.Exists(path); n++)
        {
            path = Path.Combine(folder, $"{stem} ({n}){ext}");
        }

        return path;
    }
}
