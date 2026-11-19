// Mirrors Bulwark lib/jmap/client.ts:8155-8183; Stalwart crates/jmap/src/participant_identity/{get,set}.rs.
using System.Text.Json.Nodes;
namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private (string? Name, JsonObject Args) ParticipantIdentityMethod(string name, JsonObject args) => name switch
    {
        "ParticipantIdentity/get" => ParticipantIdentityGet(args),
        "ParticipantIdentity/set" => ParticipantIdentitySet(args),
        _ => Error("unknownMethod"),
    };

    private (string? Name, JsonObject Args) ParticipantIdentityGet(JsonObject args)
    {
        if (CalendarsOf(args) is not { } cals || !options.CalendarsCapability)
        {
            return Error("unknownMethod");
        }

        if (cals.Fails)
        {
            return Error("serverFail");
        }

        return (null, new JsonObject { ["accountId"] = (string?)args["accountId"] ?? options.AccountId, ["list"] = new JsonArray([.. cals.ParticipantIdentities().Select(i => (JsonNode?)i)]), ["notFound"] = new JsonArray() });
    }

    private (string? Name, JsonObject Args) ParticipantIdentitySet(JsonObject args)
    {
        if (CalendarsOf(args) is not { } cals || !options.CalendarsCapability)
        {
            return Error("unknownMethod");
        }

        if (cals.Fails)
        {
            return Error("serverFail");
        }

        cals.SetDefaultParticipantIdentity((string?)args["onSuccessSetIsDefault"] ?? string.Empty);
        return (null, new JsonObject { ["accountId"] = (string?)args["accountId"] ?? options.AccountId });
    }
}
