#if AUTOMATION_WRITES

namespace Barbican.App.MailBody.Automation;

/// <summary>The original active script and its account identifiers; retained only in the encrypted journal.</summary>
public sealed record FilterWriteSnapshot(string AccountKey, string MailAccountId, string SieveAccountId, string ScriptId, string ScriptText, string State);

#endif
