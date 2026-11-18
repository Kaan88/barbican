// The fake server's Identity/set (RFC 8621 section 6.3: create with name, email, replyTo, bcc, textSignature, htmlSignature; update; destroy; Stalwart crates/jmap/src/identity/set.rs: an email that is not one of the account's addresses is invalidProperties, a default identity has mayDelete false and a destroy of it is forbidden; Bulwark lib/jmap/client.ts:5213-5287).
// STUB: answers unknownMethod until set-data fills it (it works on FakeJmapOptions.Identities, bumps the Identity state, raises Changed for push, FailNextSet like FakeApi.CalendarSet.cs). M6 plan section 4.12. Owner set-data.

using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private static (string? Name, JsonObject Args) IdentitySet(JsonObject args)
    {
        _ = args;
        return Error("unknownMethod");
    }
}
