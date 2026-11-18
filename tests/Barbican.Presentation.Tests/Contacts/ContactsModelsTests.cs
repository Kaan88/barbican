// New tests (M5 plan 6.2 acceptance): the group form, the directory browser, the form's card diff and the name block read from a card, the file picker queue. Owner con-ui.

#pragma warning disable MA0002, MA0006, MA0048 // ids and names are compared ordinally by nature; the fake directory sits beside its test
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Contacts;
using Barbican.Presentation.Contacts;

namespace Barbican.Presentation.Tests.Contacts;

internal sealed class FakeDirectory(bool fallback = false) : IContactDirectory
{
    public bool IsAvailable(string accountKey) => true;

    public Task<Result<(IReadOnlyList<JsonNode> Principals, bool Fallback)>> LoadAsync(string accountKey, CancellationToken ct)
    {
        IReadOnlyList<JsonNode> principals =
        [
            ContactsHarness.J("""{"id": "p2", "type": "individual", "name": "Zed Zimmer", "email": "zed@example.org"}"""),
            ContactsHarness.J("""{"id": "p1", "type": "group", "name": "All staff", "email": "staff@example.org"}"""),
            ContactsHarness.J("""{"id": "p3", "type": "individual", "name": "No Mail"}"""),
        ];
        return Task.FromResult(Result<(IReadOnlyList<JsonNode> Principals, bool Fallback)>.Ok((principals, fallback)));
    }
}

public sealed class ContactsModelsTests
{
    [Fact]
    public async Task A_new_group_is_created_with_the_uids_of_the_ticked_contacts()
    {
        using var h = new ContactsHarness();
        var model = new ContactsGroupModel(h.View, h.Writer, null, ["c2"]);
        var done = false;
        model.Finished += (_, e) => done = e.Done;
        Assert.True(model.Picks.Single(p => p.Id == "c2").Selected);
        model.Name = "  ";
        _ = await model.SaveAsync(CancellationToken.None);
        Assert.Contains("name", model.Error);
        Assert.Empty(h.Writer.Groups);
        model.Name = "Poets";
        model.Picks.Single(p => p.Id == "c1").Selected = true;
        _ = await model.SaveAsync(CancellationToken.None);
        var (name, uids) = Assert.Single(h.Writer.Groups);
        Assert.Equal("Poets", name);
        Assert.Equal(["urn:uuid:u-c1", "urn:uuid:u-c2"], uids.Order(StringComparer.Ordinal));
        Assert.True(done);
    }

    [Fact]
    public async Task Editing_a_group_sends_only_the_member_difference_and_the_new_name()
    {
        using var h = new ContactsHarness();
        var model = new ContactsGroupModel(h.View, h.Writer, "g1");
        Assert.Equal("Engines", model.Name);
        Assert.Equal(["c1", "c3"], model.Picks.Where(p => p.Selected).Select(p => p.Id).Order(StringComparer.Ordinal));
        model.Picks.Single(p => p.Id == "c1").Selected = false;
        model.Picks.Single(p => p.Id == "c2").Selected = true;
        model.Name = "Analytical Engines";
        _ = await model.SaveAsync(CancellationToken.None);
        var members = Assert.Single(h.Writer.Members);
        Assert.Equal(["urn:uuid:u-c2"], members.Add);
        Assert.Equal(["urn:uuid:u-c1"], members.Remove);
        Assert.Equal("Analytical Engines", (string?)Assert.Single(h.Writer.Updates).Patch["name"]!["full"]);
    }

    [Fact]
    public async Task The_directory_lists_principals_with_an_address_and_joins_the_ticked_ones()
    {
        var model = new ContactsDirectoryModel(new FakeDirectory(fallback: true), "acc");
        await model.LoadAsync(CancellationToken.None);
        Assert.Equal(["All staff", "Zed Zimmer"], model.Picks.Select(p => p.Name));
        Assert.Contains("does not list", model.Status);
        model.Picks[1].Selected = true;
        model.Picks[0].Selected = true;
        Assert.Equal("staff@example.org, zed@example.org", model.Chosen);
        model.Search = "zimmer";
        Assert.Equal(["Zed Zimmer"], model.Visible.Select(p => p.Name));
    }

    [Fact]
    public void The_form_reads_the_name_block_from_components_or_splits_a_plain_full_name()
    {
        var a = ContactsFormModel.FormOf(ContactsHarness.J("""{"name": {"components": [{"kind": "title", "value": "Dr"}, {"kind": "given", "value": "Ada"}, {"kind": "given2", "value": "King"}, {"kind": "surname", "value": "Lovelace"}, {"kind": "generation", "value": "Jr"}]}, "nicknames": {"n": {"name": "Countess"}}, "organizations": {"o": {"name": "Engines", "units": [{"name": "Maths"}]}}, "titles": {"t": {"name": "Analyst"}}, "notes": {"x": {"note": "hello"}}}"""));
        Assert.Equal(("Dr", "Ada", "King", "Lovelace", "Jr", "Countess"), (a.Prefix, a.Given, a.Middle, a.Surname, a.Suffix, a.Nickname));
        Assert.Equal(("Engines", "Maths", "Analyst", "hello"), (a.Org, a.Dept, a.JobTitle, a.Notes));
        var b = ContactsFormModel.FormOf(ContactsHarness.J("""{"name": {"full": "Ada King Lovelace"}}"""));
        Assert.Equal(("Ada King", "Lovelace"), (b.Given, b.Surname));
    }

    [Fact]
    public void A_save_patches_only_what_changed_and_never_the_server_owned_properties()
    {
        var original = ContactsHarness.J("""{"id": "c1", "uid": "u", "addressBookIds": {"b1": true}, "name": {"full": "Ada"}, "emails": {"e": {"address": "a@x.example"}}, "notes": {"n": {"note": "old"}}}""");
        var next = (JsonObject)ContactsHarness.J("""{"id": "c1", "uid": "u2", "kind": "individual", "addressBookIds": {"b2": true}, "name": {"full": "Ada"}, "emails": {"e": {"address": "b@x.example"}}, "keywords": {"Friends": true}}""");
        var patch = ContactsFormModel.Patch(original, next);
        Assert.Equal(["emails", "keywords", "notes"], patch.Select(p => p.Key).Order(StringComparer.Ordinal));
        Assert.Null(patch["notes"]);
        Assert.Equal("b@x.example", (string?)patch["emails"]!["e"]!["address"]);
    }

    [Fact]
    public async Task The_file_picker_answers_queued_picks_first_then_the_platform()
    {
        var calls = 0;
        var picker = new ContactsFilePicker(open: _ =>
        {
            calls++;
            return Task.FromResult<ContactsPickedFile?>(new ContactsPickedFile("platform.vcf", "x"));
        });
        picker.QueueOpen(new ContactsPickedFile("queued.vcf", "BEGIN:VCARD"));
        Assert.Equal("queued.vcf", (await picker.OpenVcardAsync(CancellationToken.None))!.Name);
        Assert.Equal(0, calls);
        Assert.Equal("platform.vcf", (await picker.OpenVcardAsync(CancellationToken.None))!.Name);
        picker.QueueSave("C:/x/out.vcf");
        Assert.Equal("C:/x/out.vcf", await picker.SaveTextAsync("contacts.vcf", "TEXT", CancellationToken.None));
        Assert.Equal("TEXT", picker.LastSaved);
        Assert.Null(await picker.SaveTextAsync("contacts.vcf", "TEXT", CancellationToken.None));
    }
}
