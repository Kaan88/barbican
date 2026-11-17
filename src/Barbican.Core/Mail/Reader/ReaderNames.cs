// Mirrors: Rust reader.rs:194 strip_subject_prefixes, :213 forward_subject, :219 sanitize_part, :233 eml_export_name, :241 bundle_name;
// Bulwark lib/subject-prefix.ts, lib/download-filename.ts, lib/attachment-name.ts, lib/forward-as-attachment.ts:43,80. Stub from stage F of M2; owner reader.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Mail.Reader;

/// <summary>Subjects and file names the reader derives from a message.</summary>
public static class ReaderNames
{
    /// <summary>The subject without Re:, Fwd:, Fw:, AW:, WG: prefixes (with an optional [n]).</summary>
    public static string StripSubjectPrefixes(string subject)
    {
        throw new PortPendingException("reader.rs:194 strip_subject_prefixes");
    }

    /// <summary>"Fwd: subject" with its prefixes stripped; empty for a message without a subject.</summary>
    public static string ForwardSubject(string subject)
    {
        throw new PortPendingException("reader.rs:213 forward_subject");
    }

    /// <summary>Characters a file cannot have become "_", runs collapse, the edges are trimmed, at most <paramref name="max"/> characters.</summary>
    public static string SanitizePart(string text, int max)
    {
        throw new PortPendingException("reader.rs:219 sanitize_part");
    }

    /// <summary>"attachments_subject.zip", or "attachments.zip" for an empty subject.</summary>
    public static string BundleName(string subject)
    {
        throw new PortPendingException("reader.rs:241 bundle_name");
    }

    /// <summary>"yyyy-MM-dd HH.mm.ss-subject.eml" from receivedAt (or sentAt) and the subject.</summary>
    public static string EmlExportName(JsonNode message)
    {
        throw new PortPendingException("reader.rs:233 eml_export_name");
    }
}
