// Mirrors: Rust invite.rs:195 parse return. Contract from the M4 plan section 4.3; owner cal-edit.

using System.Text.Json.Nodes;

namespace Barbican.Core.Calendar.Invitations;

/// <summary>The first event of a message's calendar part and its iTIP method.</summary>
public sealed record ParsedInvitation(JsonNode Event, InviteMethod Method);
