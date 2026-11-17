// Mirrors: Rust jmap.rs:1256 parse_addresses. Stub from F2; owner jmap.

using Barbican.Core.Common;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Jmap.Submission;

/// <summary>Parses a typed recipient list.</summary>
public static class AddressParser
{
    /// <summary>Splits on commas and semicolons (quotes respected) and reads <c>"Name" &lt;addr&gt;</c> forms; items without an address are dropped.</summary>
    public static IReadOnlyList<EmailAddress> Parse(string text) => throw new PortPendingException("AddressParser.Parse: jmap stream");
}
