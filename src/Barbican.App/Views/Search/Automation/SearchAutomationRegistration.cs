#if AUTOMATION
// Mirrors: spec/automation-protocol.md `search TEXT`, `suggestions`, `chips` (planned for search) and the search scenario of M2 plan 6.4: `chip-remove KEY`, `filter-toggle TOKEN`, `rule PRESET`.
// No input is produced: the commands drive the model the box draws and the quick-rule service (ADR 0007). Owner search.

using Barbican.Automation;
using Barbican.Core.Filters.QuickRules;
using Barbican.Core.Search;
using Barbican.App.Views.Menus.Automation;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Rules;
using Barbican.Presentation.Search;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.App.Views.Search.Automation;

/// <summary>Registers the search stream's automation commands.</summary>
public static class SearchAutomationRegistration
{
    // MA0051: a command table (one case per protocol command) reads top to bottom; splitting it scatters the protocol (as .editorconfig says for src/Barbican.Automation).
#pragma warning disable MA0051
    public static IServiceCollection AddBarbicanSearchAutomation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Add(services, "search", async (sp, args) =>
        {
            var model = sp.GetRequiredService<SearchBoxModel>();
            model.Edited(args);
            await model.SearchAsync();
            var list = sp.GetRequiredService<MessageListViewModel>();
            return $"ok {list.Messages.Count()} rows (total {list.Total}) chips={model.Chips.Count}";
        });
        Add(services, "rows", (sp, _) =>
        {
            // read only: what the list shows now (the feel scripts poll it while a swap draws)
            var list = sp.GetRequiredService<MessageListViewModel>();
            return Task.FromResult($"ok {list.Messages.Count()} rows (total {list.Total}) chips={sp.GetRequiredService<SearchBoxModel>().Chips.Count}");
        });
        Add(services, "snippet", (sp, args) =>
        {
            // the marks the list draws for a row (N = row index or message id): `snippet subject=[[hit]]... preview=...`, or `none` when the row has no mark
            var list = sp.GetRequiredService<MessageListViewModel>();
            var rows = list.Messages.ToList();
            var key = args.Trim();
            var row = int.TryParse(key, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) && n < rows.Count ? rows[n] : rows.Find(r => string.Equals(r.Id, key, StringComparison.Ordinal));
            if (row is null)
            {
                return Task.FromResult("error no such row");
            }

            var source = sp.GetRequiredService<Barbican.Core.Search.ISnippetSource>();
            static string Show(IReadOnlyList<Barbican.Core.Search.TextSegment>? s) => s is null ? "none" : string.Concat(s.Select(x => x.Marked ? "[[" + x.Text + "]]" : x.Text));
            return Task.FromResult($"{row.Id} subject={Show(source.SubjectFor(row.Id))} preview={Show(source.PreviewFor(row.Id))}");
        });
        Add(services, "global-search", async (sp, args) =>
        {
            var palette = sp.GetRequiredService<Barbican.Presentation.Palette.PaletteController>();
            await palette.OpenAsync(GlobalSearchPaletteProvider.ScopeName);
            await palette.SetTextAsync(args);
            var rows = palette.Rows.Where(r => !r.IsHeader).Select(r => (r.Enabled ? string.Empty : "!") + r.Command!.Label).ToList();
            return $"{rows.Count} rows: " + string.Join(" | ", rows.Take(8));
        });
        Add(services, "chips", (sp, _) =>
        {
            var chips = sp.GetRequiredService<SearchBoxModel>().Chips;
            return Task.FromResult(chips.Count == 0 ? "none" : string.Join(" | ", chips.Select(c => c.Value.Length > 0 ? $"{c.Key}={c.Label}: {c.Value}" : $"{c.Key}={c.Label}")));
        });
        Add(services, "suggestions", (sp, args) =>
        {
            var model = sp.GetRequiredService<SearchBoxModel>();
            model.Edited(args);
            var rows = model.Suggestions.Select(s => s switch
            {
                SearchSuggestion.Recent r => "recent: " + r.Query,
                SearchSuggestion.Person p => $"person: {p.Name} <{p.Email}>",
                _ => string.Empty,
            });
            return Task.FromResult(model.Suggestions.Count == 0 ? "none" : string.Join(" | ", rows));
        });
        Add(services, "chip-remove", async (sp, args) =>
        {
            var model = sp.GetRequiredService<SearchBoxModel>();
            await model.RemoveChipAsync(args.Trim());
            var list = sp.GetRequiredService<MessageListViewModel>();
            return $"ok text=\"{model.Text}\" {list.Messages.Count()} rows chips={model.Chips.Count}";
        });
        Add(services, "filter-toggle", async (sp, args) =>
        {
            var model = sp.GetRequiredService<SearchBoxModel>();
            await model.ToggleTokenAsync(args.Trim());
            return $"ok text=\"{model.Text}\"";
        });
        Add(services, "rule", async (sp, args) =>
        {
            var list = sp.GetRequiredService<MessageListViewModel>();
            var row = list.Selected ?? list.Messages.FirstOrDefault();
            if (row is null)
            {
                return "error no message";
            }

            var rules = sp.GetRequiredService<QuickRuleService>();
            var name = args.Trim().ToLowerInvariant();
            var result = await rules.ApplyAsync(
                new Barbican.Core.Mail.Model.MailTarget(string.Empty, string.Empty, row.Id, row.Summary.ThreadId ?? string.Empty, row.Id, row.Summary.MailboxIds, row.Summary.Keywords),
                _ => name switch
                {
                    "block" => rules.JunkFolder() is { } junk ? new QuickRulePreset.Block(junk) : null,
                    "mark-read" => new QuickRulePreset.MarkRead(),
                    _ => null,
                },
                CancellationToken.None);
            return result.IsOk ? "ok " + result.Value : "error " + result.Error!.Message;
        });
        return services;
    }
#pragma warning restore MA0051

    private static void Add(IServiceCollection services, string name, Func<IServiceProvider, string, Task<string>> run) =>
        services.AddSingleton<IAutomationCommand>(sp => new LambdaAutomationCommand(name, args => run(sp, args)));
}
#endif
