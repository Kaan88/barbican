// The fake server's contact writes (RFC 9610 AddressBook/set with onSuccessSetIsDefault and onDestroyRemoveContents, ContactCard/set create, update with JSON-pointer patches through FakeCalendars.ApplyPatch, destroy; Stalwart crates/jmap/src/addressbook/set.rs, contact/set.rs: a pointer into a map the card
// lacks, such as keywords/x on a card without keywords, is an invalidPatch). STUB: answers unknownMethod until con-data fills it in its first step (create with uid and addressBookIds, update, destroy, notFound and invalidProperties, ifInState, a request log and FailNextSet as FakeApi.CalendarSet.cs has). M5 plan section 4.13. Owner con-data.

using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private static (string? Name, JsonObject Args) ContactSet(string type, JsonObject args)
    {
        _ = type;
        _ = args;
        return Error("unknownMethod");
    }
}
