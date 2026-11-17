// Mirrors: RFC 8621 section 4.8 (created, notCreated). Contract from stage F of M2; owner actions.

using Barbican.Core.Jmap.Errors;

namespace Barbican.Core.Mail.Write;

/// <summary>The result of an Email/import: ids created per entry index and the refusals.</summary>
public sealed record EmailImportReport(IReadOnlyDictionary<int, string> Created, IReadOnlyList<SetError> Refused);
