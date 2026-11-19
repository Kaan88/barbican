#if AUTOMATION_WRITES

namespace Barbican.App.MailBody.Automation;

/// <summary>Recovery record: pending means a request may have reached the server without acknowledgement.</summary>
public sealed record FilterWriteRecord(FilterWriteSnapshot Original, string Token)
{
    public string? EmailId { get; set; }
    public string? FolderId { get; set; }
    public string? SavedState { get; set; }
    public bool ImportPending { get; set; }
    public bool FolderPending { get; set; }
    public bool ScriptPending { get; set; }
    public bool ApplyPending { get; set; }
}

#endif
