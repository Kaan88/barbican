// Mirrors: Bulwark components/email/email-composer.tsx attachment chips :6293-6419 (name, size, a spinner while uploading with a cancel, remove); I-252 (remove and cancel; rename, edit, preview are M5).
// Owner composer-ui.

using Barbican.Core.Compose;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Compose;

/// <summary>One attachment of the open composer: uploading, uploaded (has a blob id) or failed.</summary>
public sealed class AttachmentItem : ObservableObject
{
    private AttachmentState _state;
    private DraftAttachment _attachment;
    private string? _error;

    internal AttachmentItem(DraftAttachment attachment, AttachmentState state, CancellationTokenSource? cancel)
    {
        _attachment = attachment;
        _state = state;
        Cancel = cancel;
    }

    /// <summary>Cancels the upload in flight (null once it ended).</summary>
    internal CancellationTokenSource? Cancel { get; set; }

    public AttachmentState State
    {
        get => _state;
        internal set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsUploading));
            }
        }
    }

    public bool IsUploading => _state == AttachmentState.Uploading;

    public DraftAttachment Attachment
    {
        get => _attachment;
        internal set => SetProperty(ref _attachment, value);
    }

    public string Name => _attachment.Name;

    /// <summary>"1.2 MB".</summary>
    public string SizeText => FormatSize(_attachment.Size);

    /// <summary>Why it failed.</summary>
    public string? Error
    {
        get => _error;
        internal set => SetProperty(ref _error, value);
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.#} MB",
    };
}
