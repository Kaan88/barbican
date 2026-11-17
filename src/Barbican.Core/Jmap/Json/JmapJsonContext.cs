// Mirrors: PORT-PLAN.md §3.4 "Models" (sealed records with System.Text.Json source generation; [JsonExtensionData] so unknown
// properties survive a read-modify-write); RFC 8621 §2 (Mailbox), §4.1 (Email), §6 (Identity); Bulwark lib/jmap/types.ts. Owner jmap.

using System.Text.Json.Serialization;
using Barbican.Core.Jmap.Submission;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Jmap.Json;

/// <summary>The source-generated serializer of the JMAP models (Core is AOT-compatible: no reflection serialization).</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = [typeof(KeywordsConverter), typeof(MailboxRoleConverter)])]
[JsonSerializable(typeof(EmailSummary))]
[JsonSerializable(typeof(EmailBody))]
[JsonSerializable(typeof(Mailbox))]
[JsonSerializable(typeof(Identity))]
[JsonSerializable(typeof(SubmissionCapability))]
public sealed partial class JmapJsonContext : JsonSerializerContext;
