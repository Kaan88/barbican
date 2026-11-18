// The M5 contacts seed (M5 plan section 4.13): a Contacts book (default) and a Work book, cards with typed entries and Apple-style labels, a card with a photo, a group with members keyed by uid, a duplicate pair (the same name written two ways, one email in common) and 600 plain cards for the chunking
// (maxObjectsInGet is 500). Synthetic, example.org addresses only. A shared account's book is not seeded: the fake server serves contacts of its own account only. A test builds the server with FakeJmapOptions { ContactsCapability = true, Contacts = FakeSeed.ContactsM5() }. Owner con-data.

#pragma warning disable MA0051 // the seed is one block of sample data

using System.Globalization;
using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public static partial class FakeSeed
{
    public const string ContactAdaId = "cc-ada";

    public const string ContactBabbageId = "cc-babbage";

    public const string ContactPhotoId = "cc-photo";

    public const string ContactAnnaId = "cc-anna";

    public const string ContactAnnaTwinId = "cc-anna2";

    public const string ContactGroupId = "cc-friends";

    public const string ContactAdaUid = "urn:uuid:11111111-1111-4111-8111-111111111111";

    public const string ContactBabbageUid = "urn:uuid:22222222-2222-4222-8222-222222222222";

    /// <summary>A 1 x 1 PNG.</summary>
    public const string TinyPngDataUri = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    /// <summary>The books Contacts (default, id ab1) and Work (ab2), and the cards above; <paramref name="bulk"/> plain cards "Bulk 000" and on go to Contacts.</summary>
    public static FakeContacts ContactsM5(int bulk = 600)
    {
        var contacts = new FakeContacts();
        var main = contacts.AddBook("Contacts", isDefault: true);
        var work = contacts.AddBook("Work");
        _ = contacts.AddCard(main, new JsonObject
        {
            ["id"] = ContactAdaId,
            ["uid"] = ContactAdaUid,
            ["updated"] = "2026-06-01T09:00:00Z",
            ["name"] = new JsonObject
            {
                ["@type"] = "Name",
                ["components"] = new JsonArray(new JsonObject { ["kind"] = "given", ["value"] = "Ada" }, new JsonObject { ["kind"] = "surname", ["value"] = "Lovelace" }),
                ["isOrdered"] = true,
                ["full"] = "Ada Lovelace",
            },
            ["emails"] = new JsonObject
            {
                ["e0"] = new JsonObject { ["@type"] = "EmailAddress", ["address"] = "ada@analytical.example", ["contexts"] = new JsonObject { ["work"] = true }, ["pref"] = 1 },
                ["e1"] = new JsonObject { ["@type"] = "EmailAddress", ["address"] = "ada@home.example", ["label"] = "Old", ["x-custom"] = 7 },
            },
            ["phones"] = new JsonObject { ["p0"] = new JsonObject { ["@type"] = "Phone", ["number"] = "+44 20 5550100", ["features"] = new JsonObject { ["mobile"] = true }, ["contexts"] = new JsonObject { ["private"] = true } } },
            ["anniversaries"] = new JsonObject { ["a0"] = new JsonObject { ["@type"] = "Anniversary", ["kind"] = "birth", ["date"] = new JsonObject { ["@type"] = "PartialDate", ["year"] = 1815, ["month"] = 12, ["day"] = 10 } } },
            ["organizations"] = new JsonObject { ["o0"] = new JsonObject { ["@type"] = "Organization", ["name"] = "Analytical Engines" } },
            ["keywords"] = new JsonObject { ["VIP"] = true },
        });
        _ = contacts.AddCard(work, new JsonObject
        {
            ["id"] = ContactBabbageId,
            ["uid"] = ContactBabbageUid,
            ["updated"] = "2026-05-01T09:00:00Z",
            ["name"] = new JsonObject { ["@type"] = "Name", ["components"] = new JsonArray(new JsonObject { ["kind"] = "given", ["value"] = "Charles" }, new JsonObject { ["kind"] = "surname", ["value"] = "Babbage" }), ["full"] = "Charles Babbage" },
            ["emails"] = new JsonObject { ["e0"] = new JsonObject { ["@type"] = "EmailAddress", ["address"] = "charles@analytical.example" } },
        });
        _ = contacts.AddCard(main, new JsonObject
        {
            ["id"] = ContactPhotoId,
            ["name"] = new JsonObject { ["@type"] = "Name", ["full"] = "Pia Photo" },
            ["emails"] = new JsonObject { ["e0"] = new JsonObject { ["address"] = "pia@photo.example" } },
            ["media"] = new JsonObject { ["m0"] = new JsonObject { ["@type"] = "Media", ["kind"] = "photo", ["uri"] = TinyPngDataUri, ["mediaType"] = "image/png" } },
        });
        _ = contacts.AddCard(main, new JsonObject
        {
            ["id"] = ContactAnnaId,
            ["updated"] = "2026-01-01T00:00:00Z",
            ["name"] = new JsonObject { ["full"] = "Anna Müller" },
            ["emails"] = new JsonObject { ["e0"] = new JsonObject { ["address"] = "anna@mueller.example" } },
        });
        _ = contacts.AddCard(work, new JsonObject
        {
            ["id"] = ContactAnnaTwinId,
            ["updated"] = "2026-02-01T00:00:00Z",
            ["name"] = new JsonObject { ["@type"] = "Name", ["components"] = new JsonArray(new JsonObject { ["kind"] = "surname", ["value"] = "Muller" }, new JsonObject { ["kind"] = "given", ["value"] = "anna" }) },
            ["emails"] = new JsonObject { ["e0"] = new JsonObject { ["address"] = "ANNA@mueller.example" } },
            ["phones"] = new JsonObject { ["p0"] = new JsonObject { ["number"] = "030 5550123" } },
        });
        _ = contacts.AddCard(main, new JsonObject
        {
            ["id"] = ContactGroupId,
            ["kind"] = "group",
            ["name"] = new JsonObject { ["full"] = "Friends" },
            ["members"] = new JsonObject { [ContactAdaUid] = true, [ContactBabbageUid] = true },
        });
        for (var i = 0; i < bulk; i++)
        {
            var n = i.ToString("D3", CultureInfo.InvariantCulture);
            _ = contacts.AddCard(main, new JsonObject
            {
                ["name"] = new JsonObject { ["full"] = "Bulk " + n },
                ["emails"] = new JsonObject { ["e0"] = new JsonObject { ["address"] = "bulk" + n + "@bulk.example" } },
            });
        }

        return contacts;
    }
}
