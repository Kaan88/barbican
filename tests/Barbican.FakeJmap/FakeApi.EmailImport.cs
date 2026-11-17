// Mirrors: RFC 8621 section 4.8; Stalwart crates/jmap/src/email/import.rs. Stage F of M2 wires the dispatch arm in FakeApi.cs; this file is the stub the owner (actions) fills (M2 plan 3.1). Until then the method answers unknownMethod,
// exactly what the M1 fake did for it.

using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private static (string? Name, JsonObject Args) EmailImport(JsonObject args) => Error("unknownMethod");
}
