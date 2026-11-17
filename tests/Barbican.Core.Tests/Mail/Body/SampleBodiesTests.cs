// Every message of the FakeJmap seed goes the reader's way (MailService body fetch, body cache, cid inlining, page in both themes) without
// failing: the sample data the hidden feel runs open. Owner body.

using Barbican.Core.Mail.Body;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Service;
using Barbican.Core.Tests.Jmap;

namespace Barbican.Core.Tests.Mail.Body;

public sealed class SampleBodiesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_seeded_message_builds_a_page()
    {
        var f = await Fake.StartAsync();
        var s = new MailService(f.Client, f.Time);
        var cache = new Barbican.Core.Mail.Cache.BodyCache();
        var ids = f.Server.Store.EmailIds().ToList();
        Assert.NotEmpty(ids);
        foreach (var id in ids)
        {
            var r = await s.GetBodyAsync(id, new BodyFetchOptions(), Ct);
            Assert.True(r.IsOk, id + ": " + r.Error?.Message);
            var body = await CidImages.InlineAsync(BodyShape.Normalize(r.Value!), (_, _) => Task.FromResult<byte[]?>([1, 2, 3]), Ct);
            cache.Put("sample", body, cache.Generation);
            Assert.True(cache.TryGet("sample", id, out _));
            var (html, text) = MailHtml.BodyText(body);
            var markup = new MailContent(MailHtml.IsHtml(body) ? html : null, text).BodyMarkup(plainView: false);
            foreach (var dark in new[] { true, false })
            {
                var page = MailPageBuilder.Build(markup, new MailPageOptions { Dark = dark });
                Assert.StartsWith("<!doctype html>", page.Html, StringComparison.Ordinal);
            }

            if (body.ThreadId is { } t)
            {
                Assert.True((await s.GetThreadAsync(t, Ct)).IsOk);
            }
        }
    }

    [Fact]
    public async Task The_inline_chart_of_the_seed_is_downloaded_into_the_page()
    {
        var f = await Fake.StartAsync();
        var s = new MailService(f.Client, f.Time);
        var id = f.Server.Store.Emails(f.Server.Store.EmailIds()).Single(e => string.Equals((string?)e["subject"], "Report with attachments", StringComparison.Ordinal))["id"]!.GetValue<string>();
        var body = BodyShape.Normalize((await s.GetBodyAsync(id, new BodyFetchOptions(), Ct)).Value!);
        var errors = new List<string>();
        var inlined = await CidImages.InlineAsync(
            body,
            async (part, ct) =>
            {
                var r = await f.Client.DownloadAsync(f.Client.Session.MailAccountId, part.BlobId!, part.Name ?? "image", part.Type!, ct);
                if (!r.IsOk)
                {
                    errors.Add(r.Error!.Message);
                    return null;
                }

                using var copy = new MemoryStream();
                await r.Value!.CopyToAsync(copy, ct);
                return copy.ToArray();
            },
            Ct);
        Assert.Empty(errors);
        var (html, _) = MailHtml.BodyText(inlined);
        Assert.Contains("data:image/png;base64,", html, StringComparison.Ordinal);
        Assert.DoesNotContain("cid:", html, StringComparison.Ordinal);
    }
}
