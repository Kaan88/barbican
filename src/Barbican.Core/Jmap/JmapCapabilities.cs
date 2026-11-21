// Mirrors: RFC 8620 §2 / §9.4 and RFC 8621 §1.3 / §10 (capability URNs), RFC 9425 (quota); Stalwart's urn:stalwart:jmap;
// Rust jmap.rs:328 is_stalwart. Owner jmap.

namespace Barbican.Core.Jmap;

/// <summary>Capability URNs the mail client uses.</summary>
public static class JmapCapabilities
{
    public const string Core = "urn:ietf:params:jmap:core";

    public const string Mail = "urn:ietf:params:jmap:mail";

    public const string Submission = "urn:ietf:params:jmap:submission";

    public const string Quota = "urn:ietf:params:jmap:quota";

    /// <summary>JMAP File Storage (draft-ietf-jmap-filenode; Stalwart crates/jmap-proto/src/object/file_node.rs); the Files app and the composer's attach-from-Files entry need it.</summary>
    public const string FileNode = "urn:ietf:params:jmap:filenode";

    public const string Stalwart = "urn:stalwart:jmap";

    /// <summary>JMAP Calendars (draft-ietf-jmap-calendars; Stalwart crates/jmap/src/calendar*); added by M4 cal-data.</summary>
    public const string Calendars = "urn:ietf:params:jmap:calendars";
}
