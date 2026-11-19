#if AUTOMATION_WRITES || BARBICAN_FILTER_TESTS

namespace Barbican.App.MailBody.Automation;

/// <summary>The original active script and its account identifiers; retained only in the encrypted journal.</summary>
public sealed record FilterWriteSnapshot(string AccountKey, string MailAccountId, string SieveAccountId, string ScriptId, string ScriptText, string State);

#endif
