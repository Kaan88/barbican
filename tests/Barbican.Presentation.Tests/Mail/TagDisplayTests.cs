// New tests (M2 hotfix 2, the user: "tagging works but rows and the reader header show nothing"): which tags a row and the reader header draw.
// Bulwark lib/thread-utils.ts:267 getEmailTagIds, hooks/use-keyword-format.ts:76 sortTagIds, thread-list-item.tsx:901 showTagChips,
// email-viewer.tsx:5422 (header tags). Owner mailcore.

#pragma warning disable MA0002 // asserts compare strings ordinally by nature
using System.Text.Json.Nodes;
using Barbican.Core.Mail.Model;
using Barbican.Core.Storage;
using Barbican.Core.Settings.Accessibility;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Tests.Support;

namespace Barbican.Presentation.Tests.Mail;

public sealed class TagDisplayTests
{
    private const string TagList = """[{"id":"work","label":"Work","color":"blue"},{"id":"home","label":"Home","color":"green"}]""";

    private static MailListSettings Settings(bool chips = true) =>
        MailListSettings.Read(PrefsSnapshot.FromJson(new JsonObject { ["emailKeywords"] = JsonNode.Parse(TagList), ["showTagChips"] = chips }), default(SystemAccessibility));

    private static EmailSummary Tagged(params string[] keywords) => Mails.Make("m1") with { Keywords = Keywords.Of(["$seen", .. keywords]) };

    [Fact]
    public void Ids_come_from_label_and_legacy_color_keywords_that_are_set_each_once()
    {
        var keywords = new Keywords(new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["$label:work"] = true,
            ["$color:work"] = true,
            ["$color:red"] = true,
            ["$label:off"] = false,
            ["$seen"] = true,
            ["$label:"] = true,
        });

        Assert.Equal(["work", "red"], RowTag.IdsOf(keywords));
    }

    [Fact]
    public void A_row_names_its_tags_in_the_tag_list_order_with_unknown_tags_last()
    {
        var row = new MessageRowViewModel(Tagged("$label:zeta", "$label:home", "$label:work"), Settings().RowContextAt(Mails.Now, TimeZoneInfo.Utc));

        Assert.True(row.ShowTags);
        Assert.Equal(["Work", "Home", "zeta"], row.Tags.Select(t => t.Name));
        Assert.Equal("W", row.Tags[0].Initial);
        Assert.NotEqual(row.Tags[0].Color, row.Tags[1].Color);
        Assert.Equal(0x9ca3afu, row.Tags[2].Color);
    }

    [Fact]
    public void Show_tag_chips_off_hides_the_row_tags_but_keeps_them_for_the_reader()
    {
        var row = new MessageRowViewModel(Tagged("$label:work"), Settings(chips: false).RowContextAt(Mails.Now, TimeZoneInfo.Utc));

        Assert.False(row.ShowTags);
        Assert.Single(row.Tags);
    }

    [Fact]
    public void A_row_without_tags_shows_none()
    {
        var row = new MessageRowViewModel(Tagged(), Settings().RowContextAt(Mails.Now, TimeZoneInfo.Utc));

        Assert.False(row.ShowTags);
        Assert.Empty(row.Tags);
    }

    [Fact]
    public void A_tag_change_makes_a_different_row_so_the_list_redraws_it()
    {
        var context = Settings().RowContextAt(Mails.Now, TimeZoneInfo.Utc);
        var before = new MessageRowViewModel(Tagged(), context);
        var after = new MessageRowViewModel(Tagged("$label:work"), context);

        Assert.False(before.SameAs(after));
        Assert.True(after.SameAs(new MessageRowViewModel(Tagged("$label:work"), context)));
    }

    [Fact]
    public void The_reader_header_carries_the_tags_from_the_row_and_from_the_body()
    {
        var settings = Settings();
        var options = settings.ReaderOptionsAt(Mails.Now, TimeZoneInfo.Utc);
        var row = new MessageRowViewModel(Tagged("$label:home"), settings.RowContextAt(Mails.Now, TimeZoneInfo.Utc));

        Assert.Equal(["Home"], ReaderSnapshots.Prefill(row, options).Header.Tags.Select(t => t.Name));

        var body = new EmailBody { Id = "m1", ThreadId = "tm1", Keywords = Keywords.Of("$label:work", "$label:home") };
        Assert.Equal(["Work", "Home"], ReaderSnapshots.FromBody(body, null, options).Header.Tags.Select(t => t.Name));
    }

    [Fact]
    public void The_open_message_header_follows_its_row_when_a_tag_or_the_star_changes()
    {
        var settings = Settings();
        var context = settings.RowContextAt(Mails.Now, TimeZoneInfo.Utc);
        var reader = new ReaderViewModel();
        reader.Apply(ReaderSnapshots.Prefill(new MessageRowViewModel(Tagged(), context), settings.ReaderOptionsAt(Mails.Now, TimeZoneInfo.Utc)));
        var changed = new List<string?>();
        reader.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        reader.FollowRow(new MessageRowViewModel(Tagged("$label:work", "$flagged"), context));

        Assert.Equal(["Work"], reader.Header.Tags.Select(t => t.Name));
        Assert.True(reader.Header.Flagged);
        Assert.Contains(nameof(ReaderViewModel.Header), changed);
        Assert.Equal(BodyState.Loading, reader.BodyState);
    }

    [Fact]
    public void Another_message_row_or_an_unchanged_row_leaves_the_header_alone()
    {
        var context = Settings().RowContextAt(Mails.Now, TimeZoneInfo.Utc);
        var reader = new ReaderViewModel();
        var open = new MessageRowViewModel(Tagged("$label:work"), context);
        reader.Apply(ReaderSnapshots.Prefill(open, Settings().ReaderOptionsAt(Mails.Now, TimeZoneInfo.Utc)));
        var changed = 0;
        reader.PropertyChanged += (_, _) => changed++;

        reader.FollowRow(new MessageRowViewModel(Mails.Make("other") with { Keywords = Keywords.Of("$label:home") }, context));
        reader.FollowRow(new MessageRowViewModel(Tagged("$label:work"), context));

        Assert.Equal(0, changed);
        Assert.Equal(["Work"], reader.Header.Tags.Select(t => t.Name));
    }

    [Fact]
    public void An_empty_reader_does_not_take_a_row()
    {
        var reader = new ReaderViewModel();

        reader.FollowRow(new MessageRowViewModel(Tagged("$label:work"), Settings().RowContextAt(Mails.Now, TimeZoneInfo.Utc)));

        Assert.True(reader.IsEmpty);
        Assert.Empty(reader.Header.Tags);
    }
}
