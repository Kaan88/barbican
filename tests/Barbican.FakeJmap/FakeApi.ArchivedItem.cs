// Mirrors: Stalwart crates/jmap/src/registry/mapping/archived_item.rs (x:ArchivedItem get, query, set requestRestore). Stage F of M2 wires the dispatch arm in FakeApi.cs; this file is the stub the owner (actions) fills (M2 plan 3.1). Until then the method answers unknownMethod,
// exactly what the M1 fake did for it.

using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private static (string? Name, JsonObject Args) ArchivedItemGet(JsonObject args) => Error("unknownMethod");

    private static (string? Name, JsonObject Args) ArchivedItemQuery(JsonObject args) => Error("unknownMethod");

    private static (string? Name, JsonObject Args) ArchivedItemSet(JsonObject args) => Error("unknownMethod");
}
