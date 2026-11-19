// Mirrors: Rust chips.rs (state :14, sync :74, edited :148, commit :170, insert :204, remove :230, edit_chip :242, blurred :269, key :281); Bulwark components/email/email-composer.tsx RecipientInput
// (handleKeyDown, handleAutocomplete, insertAutocomplete, handlePaste), B-COMP-019 (the field text is the source of truth, chips are rebuilt from it), I-246..I-249.
// One recipient field of the composer (To, Cc or Bcc): the field text, its chips, the typed input and the autocomplete list. No UI types.

using Barbican.Core.Compose.Recipients;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Compose;

/// <summary>A recipient field with chips and autocomplete.</summary>
public sealed class RecipientFieldViewModel : ObservableObject
{
    private readonly Func<SuggestionSources> _sources;
    private string _text = string.Empty;
    private string _input = string.Empty;
    private IReadOnlyList<RecipientChip> _chips = [];
    private List<Suggestion> _results = [];
    private IReadOnlyList<SuggestionRow> _rows = [];
    private string _query = string.Empty;
    private int? _moved;
    private int? _armed;

    /// <summary>Creates the field; <paramref name="sources"/> is asked for every autocomplete (the composer caches the Sent-based and contact sources).</summary>
    public RecipientFieldViewModel(string name, Func<SuggestionSources> sources)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(sources);
        Name = name;
        _sources = sources;
    }

    /// <summary>"To", "Cc" or "Bcc".</summary>
    public string Name { get; }

    /// <summary>The field text (a comma list; a group in RFC 5322 group syntax): the source of truth the draft and the send read.</summary>
    public string Text => _text;

    /// <summary>The typed text that is not a chip yet.</summary>
    public string Input => _input;

    public IReadOnlyList<RecipientChip> Chips
    {
        get => _chips;
        private set => SetProperty(ref _chips, value);
    }

    public IReadOnlyList<SuggestionRow> Suggestions
    {
        get => _rows;
        private set
        {
            if (SetProperty(ref _rows, value))
            {
                OnPropertyChanged(nameof(IsOpen));
            }
        }
    }

    /// <summary>The list is open (there are suggestions).</summary>
    public bool IsOpen => _rows.Count > 0;

    /// <summary>The highlighted row: the one the arrows moved to, else Bulwark's default pick (-1: none, Enter adds the text as typed).</summary>
    public int Highlight => !IsOpen ? -1 : _moved ?? (string.Equals(_query, _input.Trim(), StringComparison.Ordinal) ? RecipientText.DefaultSuggestionIndex(_input, _results) : -1);

    /// <summary>The field text changed by the user (typing, a pick, a removal): the composer rebuilds its draft key and fires <c>onRecipientChipsChange</c>.</summary>
    public event EventHandler? TextChanged;

    /// <summary>The parsed recipients of the field.</summary>
    public IReadOnlyList<Recipient> Recipients => RecipientText.ParseList(_text);

    /// <summary>Sets the field from a seed (no change event).</summary>
    public void Seed(string text)
    {
        _text = text ?? string.Empty;
        _input = string.Empty;
        _armed = null;
        Close();
        Rebuild();
    }

    /// <summary>Replaces the field text as an edit (plugin transform, mention, drag between fields): rebuilds the chips and raises <see cref="TextChanged"/>.</summary>
    public void Replace(IReadOnlyList<Recipient> list)
    {
        ArgumentNullException.ThrowIfNull(list);
        SetList(list);
    }

    /// <summary>Adds a recipient that is not on the field yet (mention, drop of a chip from another field).</summary>
    public bool Add(Recipient recipient)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        var list = RecipientText.ParseList(_text).ToList();
        var have = RecipientText.Expand(list).Select(r => r.Email.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        if (recipient.Group is null && have.Contains(recipient.Email.Trim().ToLowerInvariant()))
        {
            return false;
        }

        list.Add(recipient);
        SetList(list);
        return true;
    }

    /// <summary>Text typed or pasted into the input. A pasted list becomes chips at once (what is no address stays in the input, Bulwark handlePaste); otherwise the suggestions follow the text.</summary>
    public void Edited(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Disarm();
        _input = text;
        var t = text.Trim();
        var multi = t.IndexOfAny(['\n', ';']) >= 0 || (t.IndexOfAny([',', ' ', '\t']) >= 0 && t.Split([' ', '\t', '\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries).Count(w => RecipientText.IsValidEmail(w.Trim('<', '>'))) >= 2);
        if (multi)
        {
            var list = RecipientText.ParseList(_text).ToList();
            var (valid, invalid) = RecipientText.SplitPasted(t, [.. list.Select(x => x.Email)]);
            if (valid.Count > 0)
            {
                list.AddRange(valid);
                _input = string.Join(' ', invalid);
                SetList(list);
                Show(_input);
                OnPropertyChanged(nameof(Input));
                return;
            }
        }

        Show(text);
        OnPropertyChanged(nameof(Input));
    }

    /// <summary>Puts whatever is typed into chips (on Enter, Tab, a comma, blur and before sending). Entries that are not addresses still become chips, marked invalid.</summary>
    public void Commit()
    {
        if (_input.Trim().Length == 0)
        {
            return;
        }

        var list = RecipientText.ParseList(_text).ToList();
        if (_input.IndexOfAny([',', ';']) >= 0)
        {
            var (valid, invalid) = RecipientText.SplitPasted(_input, [.. list.Select(x => x.Email)]);
            list.AddRange(valid);
            list.AddRange(invalid.Select(RecipientText.ParseRecipient));
        }
        else
        {
            list.Add(RecipientText.ParseRecipient(_input));
        }

        _input = string.Empty;
        OnPropertyChanged(nameof(Input));
        Close();
        SetList(list);
    }

    /// <summary>The user picked the suggestion at <paramref name="index"/> (a click, Enter or Tab on the highlight).</summary>
    public void Pick(int index)
    {
        if (index < 0 || index >= _results.Count)
        {
            return;
        }

        var s = _results[index];
        var list = RecipientText.ParseList(_text).ToList();
        if (s.Group is { } g)
        {
            if (g.Members.Count == 0)
            {
                return;
            }

            list.Add(new Recipient { Name = s.Name, Email = string.Empty, Group = g.Members });
        }
        else
        {
            list.Add(Recipient.Addr(s.Name, s.Email));
        }

        _input = string.Empty;
        OnPropertyChanged(nameof(Input));
        Close();
        SetList(list);
    }

    /// <summary>Removes the chip at <paramref name="index"/> (its × button, or a second Backspace).</summary>
    public void Remove(int index)
    {
        var list = RecipientText.ParseList(_text).ToList();
        if (index >= 0 && index < list.Count)
        {
            list.RemoveAt(index);
        }

        _armed = null;
        SetList(list);
    }

    /// <summary>Double click: a group opens up into its members; a person goes back into the input to be edited.</summary>
    public void EditChip(int index)
    {
        var list = RecipientText.ParseList(_text).ToList();
        if (index < 0 || index >= list.Count)
        {
            return;
        }

        var x = list[index];
        list.RemoveAt(index);
        if (x.Group is { } members)
        {
            var have = list.Select(y => y.Email.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
            var at = index;
            foreach (var m in members.Where(y => !have.Contains(y.Email.ToLowerInvariant())))
            {
                list.Insert(at++, m);
            }

            SetList(list);
            return;
        }

        Commit();
        list = [.. RecipientText.ParseList(_text)];
        if (index < list.Count)
        {
            list.RemoveAt(index);
        }

        _input = RecipientText.FormatRecipient(x.Name, x.Email);
        OnPropertyChanged(nameof(Input));
        SetList(list);
    }

    /// <summary>Focus left the field: commit its text unless partial text is waiting under an open list ("sa" stays); the caller delays this a little so a click on a suggestion wins.</summary>
    public void Blur()
    {
        if (ChipSources.BlurCommits(_input, IsOpen))
        {
            Commit();
        }

        Disarm();
        Close();
    }

    /// <summary>Closes the list without changing anything.</summary>
    public void Close()
    {
        if (_results.Count == 0 && _rows.Count == 0)
        {
            return;
        }

        _results = [];
        _query = string.Empty;
        _moved = null;
        Suggestions = [];
        OnPropertyChanged(nameof(Highlight));
    }

    /// <summary>Keys in the input (Bulwark RecipientInput handleKeyDown). True when handled here; <paramref name="key"/> is a name (<c>Enter</c>, <c>Tab</c>, <c>Escape</c>, <c>ArrowDown</c>, <c>ArrowUp</c>, <c>Backspace</c>) or the typed character.</summary>
    public bool Key(string key, bool shift)
    {
        ArgumentNullException.ThrowIfNull(key);
        var open = IsOpen;
        var hl = Highlight;
        if (!string.Equals(key, "Backspace", StringComparison.Ordinal))
        {
            Disarm();
        }

        switch (key)
        {
            case "Escape" when open:
                Close();
                return true;
            case "ArrowDown" when open:
                SetHighlight(Math.Min(hl + 1, _results.Count - 1));
                return true;
            case "ArrowUp" when open:
                SetHighlight(hl < 0 ? -1 : Math.Max(hl - 1, 0));
                return true;
            case "Enter":
                if (open && hl >= 0)
                {
                    Pick(hl);
                    return true;
                }

                if (_input.Trim().Length > 0)
                {
                    Commit();
                    return true;
                }

                return false;
            case "Tab" when !shift:
                // take the suggestion or the text, then let the focus move on
                TakeOrCommit();
                return false;
            case "," or ";" when RecipientText.IsValidEmail(RecipientText.ParseRecipient(_input.Trim()).Email):
                TakeOrCommit();
                return true;
            case " " when RecipientText.IsValidEmail(_input.Trim()):
                Commit();
                return true;
            case " " when _input.Length == 0:
                return true;
            case "Backspace" when _input.Length == 0:
                return BackspaceOnEmpty();
            default:
                return false;
        }
    }

    private void TakeOrCommit()
    {
        var hl = Highlight;
        if (IsOpen && hl >= 0)
        {
            Pick(hl);
        }
        else
        {
            Commit();
        }
    }

    private bool BackspaceOnEmpty()
    {
        var n = RecipientText.ParseList(_text).Count;
        if (n == 0)
        {
            return false;
        }

        if (_armed == n - 1)
        {
            Remove(n - 1);
        }
        else
        {
            _armed = n - 1;
            Rebuild();
        }

        return true;
    }

    private void SetHighlight(int index)
    {
        _moved = index;
        OnPropertyChanged(nameof(Highlight));
    }

    private void Disarm()
    {
        if (_armed is not null)
        {
            _armed = null;
            Rebuild();
        }
    }

    private void SetList(IReadOnlyList<Recipient> list)
    {
        var text = RecipientText.FormatList(list);
        var changed = !string.Equals(text, _text, StringComparison.Ordinal);
        _text = text;
        Rebuild();
        if (changed)
        {
            OnPropertyChanged(nameof(Text));
            TextChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Rebuild()
    {
        var list = RecipientText.ParseList(_text);
        if (_armed is { } a && a >= list.Count)
        {
            _armed = null;
        }

        Chips = [.. list.Select((x, i) => ChipOf(x, _armed == i))];
        OnPropertyChanged(nameof(Text));
    }

    private static RecipientChip ChipOf(Recipient x, bool armed)
    {
        if (x.Group is { } m)
        {
            var name = x.Name.Trim().Length == 0 ? "Group" : x.Name.Trim();
            return new RecipientChip(
                $"{name} ({m.Count})",
                string.Join(", ", m.Select(y => RecipientText.FormatRecipient(y.Name, y.Email))),
                RecipientText.Initials(x.Name, string.Empty),
                m.Any(y => !RecipientText.IsValidEmail(y.Email)) ? ChipKind.Invalid : ChipKind.Group,
                armed,
                m.Count);
        }

        var shown = RecipientText.ToUnicodeEmail(x.Email);
        return new RecipientChip(
            x.Name.Trim().Length == 0 ? shown : x.Name.Trim(),
            RecipientText.FormatRecipient(x.Name, x.Email),
            RecipientText.Initials(x.Name, x.Email),
            RecipientText.IsValidEmail(x.Email) ? ChipKind.Person : ChipKind.Invalid,
            armed,
            0);
    }

    private void Show(string input)
    {
        var q = input.Trim();
        if (q.Length == 0)
        {
            Close();
            return;
        }

        var on = RecipientText.Expand(RecipientText.ParseList(_text)).Select(x => x.Email.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        var res = RecipientText.Suggest(q, _sources()).Where(x => x.Group is not null || !on.Contains(x.Email.ToLowerInvariant())).ToList();
        if (res.Count == 0)
        {
            Close();
            return;
        }

        _results = res;
        _query = q;
        _moved = null;
        Suggestions = [.. res.Select(x => x.Group is { } g
            ? new SuggestionRow(x.Name, $"Group · {g.Members.Count} member{(g.Members.Count == 1 ? string.Empty : "s")}", string.Empty, true)
            : new SuggestionRow(x.Name.Length == 0 ? RecipientText.ToUnicodeEmail(x.Email) : x.Name, x.Name.Length == 0 ? string.Empty : RecipientText.ToUnicodeEmail(x.Email), RecipientText.Initials(x.Name, x.Email), false))];
        OnPropertyChanged(nameof(Highlight));
    }
}
