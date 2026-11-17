// Mirrors: Bulwark components/email/email-composer.tsx attachments (:6293-6419 chips with a cancel while uploading, :6515 Attach files, :5760 drop overlay, paste of files), templates (:6566, template-picker.tsx),
// onRecipientChipsChange (lib/plugin-hook-catalog.ts:98); I-242, I-252, I-253, I-255, I-256. Attachments, templates and the recipient hook of ComposerViewModel.

using Barbican.Core.Compose;
using Barbican.Core.Compose.Recipients;
using Barbican.Core.Editor;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Presentation.Actions;

namespace Barbican.Presentation.Compose;

public sealed partial class ComposerViewModel
{
    private TemplatePickerState? _templates;
    private bool _inRecipientHook;

    /// <summary>The template picker, or null.</summary>
    public TemplatePickerState? Templates
    {
        get => _templates;
        private set => SetProperty(ref _templates, value);
    }

    /// <summary>Some file or picture is still uploading.</summary>
    public bool HasPendingUploads => Attachments.Any(a => a.State == AttachmentState.Uploading);

    /// <summary>Attaches files (the picker, a drop, a paste): each becomes a chip that uploads; the gates (account limit, message total) and the plugin hooks are the uploader's, a refusal removes the chip and says why.</summary>
    public async Task AddFilesAsync(IReadOnlyList<AttachmentUpload> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var tasks = new List<Task>();
        foreach (var file in files)
        {
            var cancel = new CancellationTokenSource();
            var item = new AttachmentItem(new DraftAttachment { Name = file.Name, Type = file.Type, Size = file.Data.Length }, AttachmentState.Uploading, cancel);
            Attachments.Add(item);
            OnPropertyChanged(nameof(HasPendingUploads));
            tasks.Add(UploadAsync(item, file, cancel.Token));
        }

        await Task.WhenAll(tasks).ConfigureAwait(true);
    }

    /// <summary>The × of an uploaded chip, or the cancel of one that is uploading.</summary>
    public void RemoveAttachment(AttachmentItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Cancel?.Cancel();
        _ = Attachments.Remove(item);
        OnPropertyChanged(nameof(HasPendingUploads));
    }

    /// <summary>Opens the template picker over the account's templates.</summary>
    public async Task OpenTemplatesAsync()
    {
        if (_closed || Templates is not null || _c.Templates is not { } store)
        {
            return;
        }

        var list = await store.ListAsync(AccountKey, CancellationToken.None).ConfigureAwait(true);
        if (!list.IsOk)
        {
            _ = _c.Toasts?.Show(new ToastRequest("Could not load the templates: " + list.Error!.Message, IsError: true));
            return;
        }

        var picker = new TemplatePickerState(list.Value!, InsertTemplateAsync, SaveTemplateAsync, t => DeleteTemplateAsync(store, t));
        picker.CloseRequested += (_, _) => Templates = null;
        Templates = picker;
    }

    /// <summary>The picker was dismissed (Escape).</summary>
    public void CloseTemplates() => Templates?.Cancel();

    /// <summary>Inserts a template: its subject when the subject is empty, its body at the caret.</summary>
    public Task InsertTemplateAsync(MailTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (Subject.Trim().Length == 0 && template.Subject.Length > 0)
        {
            Subject = template.Subject;
        }

        IReadOnlyList<Block> blocks;
        try
        {
            blocks = template.IsHtml ? DocHtml.Import(template.Body) : [.. Doc.FromText(template.Body).Blocks];
        }
        catch (Barbican.Core.Common.PortPendingException)
        {
            blocks = [.. Doc.FromText(template.Body).Blocks];
        }

        if (Editor is { } e)
        {
            e.Session.InsertBlocks(blocks);
            e.RequestFocus();
        }
        else
        {
            var text = string.Join('\n', blocks.Select(b => b.Text));
            FallbackBody = FallbackBody.Length == 0 ? text : FallbackBody + "\n" + text;
        }

        return Task.CompletedTask;
    }

    private async Task<MailTemplate?> SaveTemplateAsync(string name)
    {
        var doc = CurrentDoc();
        var isHtml = !_plain;
        string body;
        try
        {
            body = isHtml ? DocHtml.Export(doc) : DocHtml.ToText(doc);
        }
        catch (Barbican.Core.Common.PortPendingException)
        {
            body = BodyText();
            isHtml = false;
        }

        var res = await _c.Templates!.CreateAsync(AccountKey, name, Subject, body, isHtml, CancellationToken.None).ConfigureAwait(true);
        return res.IsOk ? res.Value : null;
    }

    private async Task<bool> DeleteTemplateAsync(ITemplateStore store, MailTemplate template)
    {
        var res = await store.DeleteAsync(AccountKey, template.Id, CancellationToken.None).ConfigureAwait(true);
        return res.IsOk;
    }

    private async Task UploadAsync(AttachmentItem item, AttachmentUpload file, CancellationToken ct)
    {
        string? error = null;
        try
        {
            var res = await _c.Uploader.UploadAsync(AccountKey, file, [.. Attachments.Where(a => a.State == AttachmentState.Done).Select(a => a.Attachment), .. _inline], ct).ConfigureAwait(true);
            if (res.IsOk)
            {
                item.Attachment = res.Value!;
                item.State = AttachmentState.Done;
            }
            else
            {
                error = res.Error!.Message;
            }
        }
        catch (OperationCanceledException)
        {
            // the user cancelled: the chip is already gone
            return;
        }
#pragma warning disable CA1031 // an upload that throws is a refused upload
        catch (Exception e)
#pragma warning restore CA1031
        {
            error = e.Message;
        }
        finally
        {
            item.Cancel = null;
            OnPropertyChanged(nameof(HasPendingUploads));
        }

        if (error is not null)
        {
            _ = Attachments.Remove(item);
            _ = _c.Toasts?.Show(new ToastRequest(error, IsError: true));
        }
    }

    // the field text changed by the user: plugins may change the list (onRecipientChipsChange transform); the draft key follows by itself
    private void OnRecipientsChanged(RecipientFieldViewModel field)
    {
        if (_inRecipientHook || !_c.Hooks.HasHandlers(HookNames.OnRecipientChipsChange))
        {
            return;
        }

        _ = TransformRecipientsAsync(field);
    }

    private async Task TransformRecipientsAsync(RecipientFieldViewModel field)
    {
        var before = field.Text;
        var payload = new RecipientChipsPayload(field.Name.ToLowerInvariant(), field.Recipients);
        var after = await _c.Hooks.TransformAsync(HookNames.OnRecipientChipsChange, payload, CancellationToken.None).ConfigureAwait(true);
        if (string.Equals(field.Text, before, StringComparison.Ordinal) && !string.Equals(RecipientText.FormatList(after.Recipients), before, StringComparison.Ordinal))
        {
            // the plugin's own change does not ask the plugin again
            _inRecipientHook = true;
            try
            {
                field.Replace(after.Recipients);
            }
            finally
            {
                _inRecipientHook = false;
            }
        }
    }
}
