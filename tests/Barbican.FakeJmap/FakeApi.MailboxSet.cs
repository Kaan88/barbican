// Mirrors: RFC 8621 section 2.5; Stalwart crates/jmap/src/mailbox/set.rs (mailboxHasEmail, mailboxHasChild, onDestroyRemoveEmails). Stage F of M2 wires the dispatch arm in FakeApi.cs; this file is the stub the owner (actions) fills (M2 plan 3.1). Until then the method answers unknownMethod,
// exactly what the M1 fake did for it.

using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private static (string? Name, JsonObject Args) MailboxSet(JsonObject args) => Error("unknownMethod");
}
