// Mirrors: M4 CalendarFilePicker (queued answers for hidden runs and tests, platform functions otherwise). Owner con-ui.

using System.Collections.Concurrent;

namespace Barbican.Presentation.Contacts;

/// <summary>The default <see cref="IContactsFilePicker"/>: an answer queued by automation or a test is used first, then the platform function, else nothing was chosen.</summary>
public sealed class ContactsFilePicker : IContactsFilePicker
{
    private readonly ConcurrentQueue<ContactsPickedFile?> _opens = new();
    private readonly ConcurrentQueue<ContactsPickedImage?> _images = new();
    private readonly ConcurrentQueue<string?> _saves = new();
    private readonly Func<CancellationToken, Task<ContactsPickedFile?>>? _open;
    private readonly Func<CancellationToken, Task<ContactsPickedImage?>>? _image;
    private readonly Func<string, string, CancellationToken, Task<string?>>? _save;

    public ContactsFilePicker(Func<CancellationToken, Task<ContactsPickedFile?>>? open = null, Func<CancellationToken, Task<ContactsPickedImage?>>? image = null, Func<string, string, CancellationToken, Task<string?>>? save = null)
    {
        _open = open;
        _image = image;
        _save = save;
    }

    /// <summary>The text of the last queued save, for automation and tests.</summary>
    public string? LastSaved { get; private set; }

    /// <summary>The name of the last queued save, for automation and tests.</summary>
    public string? LastSavedName { get; private set; }

    public void QueueOpen(ContactsPickedFile? file) => _opens.Enqueue(file);

    public void QueueImage(ContactsPickedImage? image) => _images.Enqueue(image);

    /// <summary>The next save answers <paramref name="path"/> without writing (null: cancelled).</summary>
    public void QueueSave(string? path) => _saves.Enqueue(path);

    public Task<ContactsPickedFile?> OpenVcardAsync(CancellationToken ct) =>
        _opens.TryDequeue(out var file) ? Task.FromResult(file) : _open is null ? Task.FromResult<ContactsPickedFile?>(null) : _open(ct);

    public Task<ContactsPickedImage?> OpenImageAsync(CancellationToken ct) =>
        _images.TryDequeue(out var image) ? Task.FromResult(image) : _image is null ? Task.FromResult<ContactsPickedImage?>(null) : _image(ct);

    public Task<string?> SaveTextAsync(string fileName, string text, CancellationToken ct)
    {
        if (_saves.TryDequeue(out var path))
        {
            LastSaved = path is null ? null : text;
            LastSavedName = path is null ? null : fileName;
            return Task.FromResult(path);
        }

        return _save is null ? Task.FromResult<string?>(null) : _save(fileName, text, ct);
    }
}
