#pragma warning disable MA0002, MA0006 // asserts compare strings ordinally by nature
using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Editor;
using Barbican.Presentation.Compose.Pipeline;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Presentation.Tests.Compose.Pipeline;

/// <summary>The template picker (I-255, I-256): search over name, subject and category, insert with placeholders filled (auto, default, typed), save as template, delete.</summary>
public sealed class TemplatePickerViewModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class MemoryStore : ITemplateStore
    {
        public List<MailTemplate> All { get; } = [];

        public Task<Result<IReadOnlyList<MailTemplate>>> ListAsync(string accountId, CancellationToken ct) =>
            Task.FromResult(Result<IReadOnlyList<MailTemplate>>.Ok([.. All.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)]));

        public Task<Result<MailTemplate>> CreateAsync(string accountId, string name, string subject, string body, bool isHtml, CancellationToken ct)
        {
            var t = new MailTemplate("id" + All.Count, name, subject, body, isHtml);
            All.Add(t);
            return Task.FromResult(Result<MailTemplate>.Ok(t));
        }

        public Task<Result<Unit>> DeleteAsync(string accountId, string templateId, CancellationToken ct)
        {
            All.RemoveAll(t => t.Id == templateId);
            return Task.FromResult(Result<Unit>.Ok(default));
        }
    }

    private sealed class PlainCodec : IDocCodec
    {
        public string Export(Doc doc) => "<p>" + ToText(doc) + "</p>";

        public string ToText(Doc doc) => string.Join('\n', doc.Blocks.Select(b => b.Text));

        public IReadOnlyList<Block> Import(string html) => [Block.Para(html)];

        public IReadOnlyList<Block> ImportText(string text) => [.. text.Split('\n').Select(Block.Para)];

        public IReadOnlyList<Block> ImportWithQuotes(string html) => Import(html);

        public string CleanQuotedHtml(string html) => html;

        public string HtmlText(string html) => html;
    }

    private static async Task<(TemplatePickerViewModel Vm, MemoryStore Store)> StartAsync()
    {
        var store = new MemoryStore();
        store.All.Add(new MailTemplate("a", "Welcome", "Hello {{recipient_name:friend}}", "Dear {{recipient_name:friend}},\nToday is {{date}}. Ref {{ticket}}\n{{sender_name}}", false) { Category = "Sales" });
        store.All.Add(new MailTemplate("b", "Invoice", "Your invoice", "<p>Total {{total:0}}</p>", true));
        var vm = new TemplatePickerViewModel(store, new PlainCodec(), new FakeTimeProvider(new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero))) { SenderName = "Ada" };
        Assert.True(await vm.LoadAsync("acct", Ct));
        return (vm, store);
    }

    [Fact]
    public async Task The_list_is_sorted_and_the_search_matches_name_subject_and_category()
    {
        var (vm, _) = await StartAsync();
        Assert.Equal(["Invoice", "Welcome"], vm.Items.Select(t => t.Name));
        vm.SearchText = "sales";
        Assert.Equal(["Welcome"], vm.Items.Select(t => t.Name));
        vm.SearchText = "your inv";
        Assert.Equal(["Invoice"], vm.Items.Select(t => t.Name));
        vm.SearchText = "zzz";
        Assert.Empty(vm.Items);
    }

    [Fact]
    public async Task Insert_fills_the_date_and_sender_then_defaults_then_what_was_typed()
    {
        var (vm, _) = await StartAsync();
        var welcome = vm.Items.Single(t => t.Name == "Welcome");
        Assert.Equal([("recipient_name", "friend"), ("ticket", "")], vm.PlaceholdersToAsk(welcome));
        var plain = vm.Insert(welcome);
        Assert.Equal("Hello friend", plain.Subject);
        Assert.Equal(["Dear friend,", "Today is Tuesday, 17 November 2026. Ref {{ticket}}", "Ada"], plain.Blocks.Select(b => b.Text));
        var typed = vm.Insert(welcome, new Dictionary<string, string> { ["recipient_name"] = "Bo", ["ticket"] = "T-9", ["total"] = "" });
        Assert.Equal("Hello Bo", typed.Subject);
        Assert.Contains("Ref T-9", typed.Blocks[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_html_template_escapes_what_is_typed()
    {
        var (vm, _) = await StartAsync();
        var invoice = vm.Items.Single(t => t.Name == "Invoice");
        var ins = vm.Insert(invoice, new Dictionary<string, string> { ["total"] = "<5 & 6>" });
        Assert.Equal("<p>Total &lt;5 &amp; 6&gt;</p>", ins.Blocks.Single().Text);
        Assert.Equal("Your invoice", ins.Subject);
    }

    [Fact]
    public async Task Save_as_template_keeps_the_html_or_the_text_and_checks_the_name()
    {
        var (vm, store) = await StartAsync();
        var body = new Doc([Block.Para("Line one"), Block.Para("Line two")]);
        Assert.False((await vm.SaveAsync("  ", "s", body, false, Ct)).IsOk);
        Assert.Equal("Give the template a name.", vm.Error);
        Assert.True((await vm.SaveAsync(" Mine ", "Subj", body, false, Ct)).IsOk);
        Assert.Equal(("Mine", "<p>Line one\nLine two</p>", true), (store.All[^1].Name, store.All[^1].Body, store.All[^1].IsHtml));
        Assert.True((await vm.SaveAsync("Text one", "Subj", body, true, Ct)).IsOk);
        Assert.Equal(("Line one\nLine two", false), (store.All[^1].Body, store.All[^1].IsHtml));
        Assert.Contains(vm.Items, t => t.Name == "Mine");
    }

    [Fact]
    public async Task Delete_removes_it_from_the_store_and_the_list()
    {
        var (vm, store) = await StartAsync();
        Assert.True((await vm.DeleteAsync(vm.Items[0], Ct)).IsOk);
        Assert.Single(store.All);
        Assert.Single(vm.Items);
    }
}
