// Mirrors: RFC 8621 section 5; Stalwart crates/jmap/src/email/snippet.rs. Stage F of M2 wires the dispatch arm in FakeApi.cs; this file is the stub the owner (search) fills (M2 plan 3.1). Until then the method answers unknownMethod,
// exactly what the M1 fake did for it.

using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private static (string? Name, JsonObject Args) SearchSnippetGet(JsonObject args) => Error("unknownMethod");
}
