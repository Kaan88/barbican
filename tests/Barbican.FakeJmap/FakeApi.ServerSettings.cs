// The M6 arms of the fake server's method switch (M6 plan section 4.12), kept out of FakeApi.Method (it is at the analyzer's length limit): VacationResponse is srv-data's (FakeApi.Vacation.cs); Identity/set and the x:AccountPassword, x:AccountSettings, x:AppPassword and x:ApiKey management objects are set-data's (FakeApi.IdentitySet.cs, FakeApi.AccountSecurity.cs).
// Frozen in the M6 plan commit; owner integrator. An unknown method still answers unknownMethod.

using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private (string? Name, JsonObject Args) ServerSettingsMethod(string name, JsonObject args) => name switch
    {
        "VacationResponse/get" => VacationGet(args),
        "VacationResponse/set" => VacationSet(args),
        "Identity/set" => IdentitySet(args),
        "x:AccountPassword/get" or "x:AccountPassword/set" or "x:AccountSettings/get" or "x:AppPassword/query" or "x:AppPassword/get" or "x:AppPassword/set" or "x:ApiKey/query" or "x:ApiKey/get" or "x:ApiKey/set" => AccountSecurityMethod(name, args),
        _ => Error("unknownMethod"),
    };
}
