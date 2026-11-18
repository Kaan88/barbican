// M5 con-data: photos of cards over FakeJmap (B-CON-014, B-CON-015, J-036): a picked picture is shrunk, uploaded as a blob and set as media; a server that refuses a blobId in media gets a data: URI; the picture is read back from either; a link photo is never fetched; export inlines blob photos.

#pragma warning disable MA0006, MA0002 // assertions compare string literals

using System.Text.Json.Nodes;
using Barbican.Core.Contacts;
using Barbican.FakeJmap;
using SkiaSharp;

namespace Barbican.Core.Tests.Contacts;

public sealed class ContactPhotosTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static byte[] Picture(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static async Task<(string Key, FakeContacts Contacts, ContactStore Store, ContactPhotos Photos)> StartAsync(bool refuseBlobs)
    {
        var contacts = FakeSeed.ContactsM5(0);
        contacts.RefusesBlobMedia = refuseBlobs;
        var rig = ContactRig.Create();
        var (key, _, _) = await rig.AddAsync("user@example.org", contacts);
        var store = new ContactStore(rig, rig.Hooks, rig.Time);
        _ = await store.LoadAsync(key, Ct);
        var writer = new ContactWriter(rig, store, new AddressBooks(rig, store), rig.Hooks);
        return (key, contacts, store, new ContactPhotos(rig, store, writer));
    }

    [Fact]
    public async Task A_picture_is_shrunk_uploaded_and_set_as_a_blob_photo_and_read_back()
    {
        var (key, contacts, store, photos) = await StartAsync(refuseBlobs: false);
        var r = await photos.SetAsync(key, FakeSeed.ContactBabbageId, Picture(900, 600), Ct);
        Assert.True(r.IsOk);
        var media = (JsonObject)contacts.Card(FakeSeed.ContactBabbageId)["media"]!["m0"]!;
        Assert.Equal("photo", (string?)media["kind"]);
        Assert.NotNull(media["blobId"]);
        Assert.Equal("image/jpeg", (string?)media["mediaType"]);

        var card = store.Snapshot(key)!.Cards.First(c => (string?)c["id"] == FakeSeed.ContactBabbageId);
        var bytes = await photos.BytesAsync(key, card, Ct);
        Assert.True(bytes.IsOk);
        Assert.Equal("image/jpeg", bytes.Value.MediaType);
        Assert.Equal(0xFF, bytes.Value.Bytes[0]);
        Assert.Equal(0xD8, bytes.Value.Bytes[1]);
    }

    [Fact]
    public async Task A_server_that_refuses_a_blob_in_media_gets_the_picture_inline_and_the_old_photo_is_replaced()
    {
        var (key, contacts, store, photos) = await StartAsync(refuseBlobs: true);
        var r = await photos.SetAsync(key, FakeSeed.ContactPhotoId, Picture(64, 64), Ct);
        Assert.True(r.IsOk);
        var media = contacts.Card(FakeSeed.ContactPhotoId)["media"]!.AsObject();
        var only = Assert.Single(media);
        Assert.Equal("m0", only.Key);
        Assert.StartsWith("data:image/jpeg;base64,", (string?)only.Value!["uri"], StringComparison.Ordinal);
        Assert.Null(only.Value["blobId"]);

        var card = store.Snapshot(key)!.Cards.First(c => (string?)c["id"] == FakeSeed.ContactPhotoId);
        Assert.True((await photos.BytesAsync(key, card, Ct)).IsOk);

        // a picture that is not an image is refused before anything is sent
        Assert.False((await photos.SetAsync(key, FakeSeed.ContactPhotoId, "nope"u8.ToArray(), Ct)).IsOk);
    }

    [Fact]
    public async Task A_photo_is_removed_and_a_link_photo_is_never_fetched()
    {
        var (key, contacts, store, photos) = await StartAsync(refuseBlobs: false);
        Assert.True((await photos.RemoveAsync(key, FakeSeed.ContactPhotoId, Ct)).IsOk);
        Assert.Empty(contacts.Card(FakeSeed.ContactPhotoId)["media"]!.AsObject());
        Assert.True((await photos.RemoveAsync(key, FakeSeed.ContactBabbageId, Ct)).IsOk);

        var link = new JsonObject { ["media"] = new JsonObject { ["m0"] = new JsonObject { ["kind"] = "photo", ["uri"] = "https://tracker.example/pixel.png" } } };
        var r = await photos.BytesAsync(key, link, Ct);
        Assert.False(r.IsOk);
        Assert.Contains("link", r.Error!.Message, StringComparison.Ordinal);
        Assert.False((await photos.BytesAsync(key, new JsonObject(), Ct)).IsOk);
        Assert.NotNull(store.Snapshot(key));
    }

    [Fact]
    public async Task An_export_inlines_blob_photos_into_copies_and_leaves_the_cards_alone()
    {
        var (key, _, store, photos) = await StartAsync(refuseBlobs: false);
        _ = await photos.SetAsync(key, FakeSeed.ContactBabbageId, Picture(40, 40), Ct);
        var card = store.Snapshot(key)!.Cards.First(c => (string?)c["id"] == FakeSeed.ContactBabbageId);
        var inlined = await photos.InlineForExportAsync(key, [card], Ct);
        var copy = Assert.Single(inlined.Value!);
        Assert.StartsWith("data:image/jpeg;base64,", (string?)copy["media"]!["m0"]!["uri"], StringComparison.Ordinal);
        Assert.Null(copy["media"]!["m0"]!["blobId"]);
        Assert.NotNull(card["media"]!["m0"]!["blobId"]);
        var vcf = VCardWriter.Generate(inlined.Value!);
        Assert.Contains("PHOTO:data:image/jpeg;base64,", vcf, StringComparison.Ordinal);
    }
}
