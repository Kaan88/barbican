// Mirrors: Rust search_ui.rs:27 edited, :67 accept, :79 pick, :85 remove, :95 searched, :104 refresh_chips, :117 remove_chip; Bulwark components/search/search-box.tsx, search-suggestions.tsx,
// search-chips.tsx ("Clear all" from two chips); I-272 (typing, Enter, empty Enter, clear X), I-273, I-274 (suggestions), I-275 (chips), I-276 (filter toggle with count), I-277..I-280 (filter panel).
// The text is the only state: chips are read from it, removing a chip rewrites it, the filter panel edits its operators, and every change runs the search again.

using Barbican.Core.Search;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Search;

/// <summary>The search box's behaviour: suggestions, chips and the filter panel over the typed text.</summary>
public sealed class SearchBoxModel : ObservableObject
{
    private readonly Func<string, Task> _run;
    private readonly SearchHistoryStore _history;
    private readonly Func<IReadOnlyList<(string Name, string Email)>> _people;
    private string _text = string.Empty;
    private string _searched = string.Empty;
    private IReadOnlyList<SearchSuggestion> _suggestions = [];
    private int _active = -1;
    private bool _hidden = true;
    private bool _filterOpen;

    /// <param name="run">Runs a search over the list (<c>MessageListViewModel.SearchAsync</c>); empty text goes back to the folder.</param>
    /// <param name="history">The recent searches.</param>
    /// <param name="people">The people that suggestions may offer (contacts, then the senders of the loaded mail).</param>
    public SearchBoxModel(Func<string, Task> run, SearchHistoryStore history, Func<IReadOnlyList<(string Name, string Email)>> people)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(people);
        _run = run;
        _history = history;
        _people = people;
    }

    /// <summary>What the box holds.</summary>
    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value ?? string.Empty);
    }

    /// <summary>The removable chips of the search that last ran.</summary>
    public IReadOnlyList<SearchChip> Chips => GlobalSearch.Chips(GlobalSearch.Parse(_searched));

    /// <summary>The number the filter toggle shows (one per chip).</summary>
    public int FilterCount => Chips.Count;

    /// <summary>The suggestions under the box (recent searches, people).</summary>
    public IReadOnlyList<SearchSuggestion> Suggestions => _suggestions;

    /// <summary>The highlighted suggestion, -1 for none (the typed text then).</summary>
    public int Active => _active;

    /// <summary>The dropdown is shown (there are suggestions and it was not dismissed).</summary>
    public bool SuggestionsOpen => !_hidden && _suggestions.Count > 0;

    /// <summary>The filter panel is open.</summary>
    public bool FilterOpen
    {
        get => _filterOpen;
        set => SetProperty(ref _filterOpen, value);
    }

    /// <summary>The text changed or the box got focus: the suggestions follow.</summary>
    public void Edited(string text)
    {
        Text = text;
        var recent = _history.Load();
        var people = string.IsNullOrWhiteSpace(text) ? [] : SearchHistory.PeopleMatching(text, _people());
        _suggestions = SearchHistory.Build(text, recent, people);
        _active = -1;
        _hidden = false;
        Changed();
    }

    /// <summary>Down enters the list, up leaves it; false when there is no list to move in.</summary>
    public bool MoveActive(int delta)
    {
        if (!SuggestionsOpen)
        {
            return false;
        }

        _active = delta > 0 ? Math.Min(_active + 1, _suggestions.Count - 1) : Math.Max(_active - 1, -1);
        OnPropertyChanged(nameof(Active));
        return true;
    }

    /// <summary>Hides the dropdown (Escape, a pick, a search).</summary>
    public void Dismiss()
    {
        if (_hidden)
        {
            return;
        }

        _hidden = true;
        _active = -1;
        Changed();
    }

    /// <summary>Enter in the box: a highlighted suggestion wins over searching the typed text.</summary>
    public async Task<bool> AcceptAsync()
    {
        if (_active < 0)
        {
            return false;
        }

        await PickAsync(_active).ConfigureAwait(true);
        return true;
    }

    /// <summary>Runs the n-th suggestion: a recent search as it was, a person as from:address.</summary>
    public async Task PickAsync(int index)
    {
        if (index < 0 || index >= _suggestions.Count)
        {
            return;
        }

        var query = _suggestions[index] switch
        {
            SearchSuggestion.Recent r => r.Query,
            SearchSuggestion.Person p => SearchHistory.PersonQuery(p.Email),
            _ => string.Empty,
        };
        Text = query;
        await SearchAsync().ConfigureAwait(true);
    }

    /// <summary>Takes a recent search out of the history.</summary>
    public void RemoveRecent(int index)
    {
        if (index >= 0 && index < _suggestions.Count && _suggestions[index] is SearchSuggestion.Recent r)
        {
            _history.Remove(r.Query);
            Edited(_text);
        }
    }

    /// <summary>Enter: remembers a non-empty search, runs it, shows its chips. An empty Enter goes back to the folder.</summary>
    public async Task SearchAsync()
    {
        var text = _text;
        if (text.Trim().Length > 0)
        {
            _history.Add(text);
        }

        _searched = text;
        _hidden = true;
        _active = -1;
        Changed();
        await _run(text).ConfigureAwait(true);
    }

    /// <summary>The clear X (and Escape on a filled box): empties the text and goes back to the folder.</summary>
    public async Task ClearAsync()
    {
        Text = string.Empty;
        await SearchAsync().ConfigureAwait(true);
    }

    /// <summary>The list moved the search text (a folder opened): the box and its chips follow, without running anything.</summary>
    public void Sync(string text)
    {
        _searched = text ?? string.Empty;
        Text = _searched;
        Changed();
    }

    /// <summary>Removes one chip (the operators of its key) and searches again; an empty key removes them all.</summary>
    public async Task RemoveChipAsync(string key)
    {
        var text = _text;
        if (key.Length == 0)
        {
            foreach (var chip in Chips)
            {
                text = GlobalSearch.WithoutOperator(text, chip.Key);
            }
        }
        else
        {
            text = GlobalSearch.WithoutOperator(text, key);
        }

        Text = text;
        await SearchAsync().ConfigureAwait(true);
    }

    /// <summary>Whether the text holds the operator token (is:unread), whatever its case.</summary>
    public bool HasToken(string token) => GlobalSearch.Tokenize(_text).Any(t => string.Equals(t.Original, token, StringComparison.OrdinalIgnoreCase));

    /// <summary>The filter panel's toggle: adds the token (taking out the ones that exclude it) or removes it, and searches again live.</summary>
    public async Task ToggleTokenAsync(string token, params string[] excludes)
    {
        var has = HasToken(token);
        var tokens = GlobalSearch.Tokenize(_text).Select(t => t.Original)
            .Where(t => !string.Equals(t, token, StringComparison.OrdinalIgnoreCase) && !excludes.Contains(t, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (!has)
        {
            tokens.Add(token);
        }

        Text = string.Join(' ', tokens);
        await SearchAsync().ConfigureAwait(true);
    }

    /// <summary>The filter panel's value fields (after:, before:, in:): replaces the operator, or removes it for an empty value, and searches again live.</summary>
    public async Task SetOperatorAsync(string key, string value)
    {
        var rest = GlobalSearch.WithoutOperator(_text, key);
        var v = value.Trim();
        Text = v.Length == 0 ? rest : (rest.Length == 0 ? $"{key}:{v}" : $"{rest} {key}:{v}");
        await SearchAsync().ConfigureAwait(true);
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(Suggestions));
        OnPropertyChanged(nameof(Active));
        OnPropertyChanged(nameof(SuggestionsOpen));
        OnPropertyChanged(nameof(Chips));
        OnPropertyChanged(nameof(FilterCount));
    }
}
