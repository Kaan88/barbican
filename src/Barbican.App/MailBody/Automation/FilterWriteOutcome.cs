#if AUTOMATION_WRITES || BARBICAN_FILTER_TESTS

namespace Barbican.App.MailBody.Automation;

/// <summary>Only aggregate results are returned to automation.</summary>
public sealed record FilterWriteOutcome(bool Passed, bool CleanupComplete);

#endif
