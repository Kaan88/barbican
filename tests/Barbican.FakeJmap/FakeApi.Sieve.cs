// Mirrors: Stalwart crates/jmap/src/sieve/{get,set,validate}.rs (onSuccessActivateScript). Stage F of M2 wires the dispatch arm in FakeApi.cs; this file is the stub the owner (search) fills (M2 plan 3.1). Until then the method answers unknownMethod,
// exactly what the M1 fake did for it.

using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private static (string? Name, JsonObject Args) SieveScriptGet(JsonObject args) => Error("unknownMethod");

    private static (string? Name, JsonObject Args) SieveScriptSet(JsonObject args) => Error("unknownMethod");

    private static (string? Name, JsonObject Args) SieveScriptValidate(JsonObject args) => Error("unknownMethod");
}
