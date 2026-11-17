// Mirrors: Rust autosave.rs (the 500 ms tick, QUIET 2 s, one save at a time, a fresh composer and its quote are no reason to keep a draft); Bulwark components/email/email-composer.tsx (autosave debounce, status :5660);
// M3 plan section 6.4 step 3 (the content key with a Session.Version fast path, inline images uploaded and handed back with SetImageUploaded); I-238; onDraftChange.
// The tick, the content key, the draft save and the inline image uploads of ComposerViewModel.

using Barbican.Core.Compose;
using Barbican.Core.Editor;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;

namespace Barbican.Presentation.Compose;

public sealed partial class ComposerViewModel
{
    private readonly AutosaveState _autosave;
    private readonly HashSet<string> _inlineStarted = new(StringComparer.Ordinal);
    private readonly List<DraftAttachment> _inline = [];
    private bool _closed;
    private ulong _lastNotified;
    private Task? _saveTask;
    private long _bodyVersion = -1;
    private string _bodyText = string.Empty;

    /// <summary>The composer's timer calls this every <see cref="AutosaveState.Tick"/> (500 ms) on the UI thread: it fires <c>onDraftChange</c>, uploads inline images and saves the draft when it is due.</summary>
    public void Tick()
    {
        if (_closed || IsSending)
        {
            return;
        }

        var key = ComputeKey();
        if (key != _lastNotified)
        {
            _lastNotified = key;
            _c.Hooks.Fire(HookNames.OnDraftChange, () => new DraftViewPayload(Id, To.Text, Cc.Text, Bcc.Text, Subject, BodyText()));
        }

        UploadInlineImages();
        if (_autosave.Observe(key) && !_autosave.InFlight)
        {
            _saveTask = SaveCoreAsync(key, explicitSave: false);
        }
    }

    /// <summary>Ctrl+S, "Save draft" in the leave dialog: saves now (a second save waits for the one in flight).</summary>
    public async Task<bool> SaveDraftAsync(bool close)
    {
        if (_closed)
        {
            return true;
        }

        await WaitForSaveAsync().ConfigureAwait(true);
        CommitInputs();
        var ok = await SaveCoreAsync(ComputeKey(), explicitSave: true).ConfigureAwait(true);
        if (ok && close)
        {
            Finish(sent: false);
        }

        return ok;
    }

    /// <summary>The content key: a hash of the fields, the options, the attachments and the exported body; the body part is recomputed only when the session's version moved.</summary>
    public ulong ComputeKey()
    {
        var h = ComposerKey.Start();
        foreach (var part in new[] { To.Text, Cc.Text, Bcc.Text, Subject, _identityId, _fromOverride ? _fromEmail : null, _fromOverride ? _fromName : null, _tag })
        {
            h = ComposerKey.Add(h, part);
        }

        h = ComposerKey.Add(h, _plain ? "p" : "r");
        h = ComposerKey.Add(h, _readReceipt ? "rr" : "");
        h = ComposerKey.Add(h, _priority.ToString());
        foreach (var a in Attachments.Where(a => a.State == AttachmentState.Done))
        {
            h = ComposerKey.Add(ComposerKey.Add(h, a.Name), a.Attachment.BlobId);
        }

        return ComposerKey.Add(h, BodyKey());
    }

    /// <summary>The body as plain text (the draft hook and the blank check).</summary>
    public string BodyText()
    {
        var doc = CurrentDoc();
        try
        {
            return DocHtml.ToText(doc);
        }
        catch (Barbican.Core.Common.PortPendingException)
        {
            return doc.TextRange(new Pos(0, 0), doc.End);
        }
    }

    /// <summary>Waits for the save in flight (the leave guard, a send and quitting do).</summary>
    public async Task WaitForSaveAsync()
    {
        if (_saveTask is { IsCompleted: false } t)
        {
            await t.ConfigureAwait(true);
        }
    }

    /// <summary>Quitting: waits for the save in flight, then saves what is edited and not on the server (a silent save, no question: the window is closing).</summary>
    public async Task FlushAsync()
    {
        await WaitForSaveAsync().ConfigureAwait(true);
        if (_closed || IsSending)
        {
            return;
        }

        var key = ComputeKey();
        if (!_autosave.Unedited(key) && !_autosave.IsSaved(key) && !IsBlankNow())
        {
            _ = await SaveCoreAsync(key, explicitSave: false).ConfigureAwait(true);
        }
    }

    private string BodyKey()
    {
        if (Editor is { } e)
        {
            var v = e.Session.Version;
            if (v != _bodyVersion)
            {
                _bodyText = ExportBody(e.Session.Doc);
                _bodyVersion = v;
            }

            return _bodyText;
        }

        return _fallbackBody;
    }

    private static string ExportBody(Doc doc)
    {
        try
        {
            return DocHtml.Export(doc);
        }
        catch (Barbican.Core.Common.PortPendingException)
        {
            return doc.TextRange(new Pos(0, 0), doc.End);
        }
    }

    private async Task<bool> SaveCoreAsync(ulong key, bool explicitSave)
    {
        var gen = _autosave.BeginSave(key);
        Status = "Saving…";
        string? error = null;
        string? draftId = null;
        try
        {
            var res = await _c.Drafts.SaveAsync(BuildDraft(commit: false), autosave: !explicitSave, CancellationToken.None).ConfigureAwait(true);
            if (res.IsOk)
            {
                draftId = res.Value!.DraftId;
            }
            else
            {
                error = res.Error!.Message;
            }
        }
#pragma warning disable CA1031 // a failed save is a status line, never a crash of the composer
        catch (Exception e) when (e is not OperationCanceledException)
#pragma warning restore CA1031
        {
            error = e.Message;
        }

        _ = _autosave.Finish(gen, error is null ? key : null);
        if (error is null)
        {
            DraftId = draftId;
            Status = "Draft saved " + _c.Time.GetLocalNow().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            if (explicitSave)
            {
                _ = _c.Toasts?.Show(new Barbican.Presentation.Actions.ToastRequest("Draft saved"));
            }

            return true;
        }

        Status = "Not saved";
        if (explicitSave)
        {
            _ = _c.Toasts?.Show(new Barbican.Presentation.Actions.ToastRequest("Could not save the draft: " + error, IsError: true));
        }

        return false;
    }

    private bool IsBlankNow() => ComposeRules.IsBlank([To.Text, Cc.Text, Bcc.Text, Subject], BodyText(), _seed.SignatureTexts, Attachments.Count + _inline.Count);

    // inline images (pasted, dropped, from a URL) hold their bytes until they are uploaded; the document then points at the blob (SetImageUploaded)
    private void UploadInlineImages()
    {
        if (Editor is null)
        {
            return;
        }

        foreach (var img in Editor.Session.InlineImages.Where(i => i.Bytes is not null && i.BlobId is null && i.Cid is not null))
        {
            if (_inlineStarted.Add(img.Cid!))
            {
                _ = UploadInlineAsync(img);
            }
        }
    }

    /// <summary>An inline image still has to be uploaded (the send waits for it).</summary>
    private bool HasPendingInline() => Editor is { } e && e.Session.InlineImages.Any(i => i.Bytes is not null && i.BlobId is null && i.Cid is not null);

    private async Task UploadInlineAsync(ImageData img)
    {
        var name = img.Name.Length > 0 ? img.Name : img.Cid + ".png";
        var upload = new AttachmentUpload(name, img.Mime.Length > 0 ? img.Mime : "image/png", img.Bytes!) { Cid = img.Cid };
        try
        {
            var res = await _c.Uploader.UploadAsync(AccountKey, upload, [.. Attachments.Select(a => a.Attachment), .. _inline], CancellationToken.None).ConfigureAwait(true);
            if (res.IsOk && res.Value!.BlobId is { } blob)
            {
                _inline.Add(res.Value);
                _ = Editor?.Session.SetImageUploaded(img.Cid!, blob, res.Value.Type, name);
                OnPropertyChanged(nameof(Attachments));
                return;
            }

            _ = _c.Toasts?.Show(new Barbican.Presentation.Actions.ToastRequest(res.Error?.Message ?? "The picture could not be uploaded.", IsError: true));
        }
#pragma warning disable CA1031 // an upload that throws is reported like one that fails
        catch (Exception e) when (e is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _ = _c.Toasts?.Show(new Barbican.Presentation.Actions.ToastRequest(e.Message, IsError: true));
        }
    }
}
