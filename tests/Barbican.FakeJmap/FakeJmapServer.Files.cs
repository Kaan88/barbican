// The filenode capability object and the accounts that share their files with the login (M5 files-data). Mirrors Stalwart 0.16.6+ (forbiddenNameChars, forbiddenNodeNames, maxSizeFileNodeName in the capability) and 1.0 / draft 18
// (supportedNodeTypes), crates/jmap/src/file/. Owner files-data.

using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeJmapServer
{
    private static JsonObject FileCapability(FakeJmapOptions o)
    {
        var cap = new JsonObject();
        if (o.Files is not { } files)
        {
            return cap;
        }

        if (files.NameRules)
        {
            cap["forbiddenNameChars"] = FakeFiles.ForbiddenChars;
            cap["forbiddenNodeNames"] = new JsonArray("con", "aux", "nul", "prn");
            cap["maxSizeFileNodeName"] = 255;
        }

        if (files.Draft18)
        {
            cap["supportedNodeTypes"] = new JsonArray("File", "Directory");
        }

        return cap;
    }

    // the accounts that share their files with the login (the filenode capability only)
    private static void AddSharedFileAccounts(JsonObject session, FakeJmapOptions o)
    {
        if (!o.FileNodeCapability || o.Files is not { } files)
        {
            return;
        }

        foreach (var id in files.SharedAccounts)
        {
            session["accounts"]![id] = new JsonObject { ["name"] = id, ["isPersonal"] = false, ["isReadOnly"] = false, ["accountCapabilities"] = new JsonObject { ["urn:ietf:params:jmap:filenode"] = FileCapability(o) } };
        }
    }
}
