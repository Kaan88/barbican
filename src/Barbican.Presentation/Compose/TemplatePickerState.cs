// Mirrors: Bulwark components/email/template-picker.tsx (a search over name, subject and category; Insert; Save as template; Delete); I-255, I-256. The template picker of the composer; the store is send-pipeline's ITemplateStore.

using Barbican.Core.Compose;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Compose;

/// <summary>The state of the template picker: the filtered list, the name for "Save as template" and the three actions.</summary>
public sealed class TemplatePickerState : ObservableObject
{
    private readonly List<MailTemplate> _all;
    private readonly Func<MailTemplate, Task> _insert;
    private readonly Func<string, Task<MailTemplate?>> _save;
    private readonly Func<MailTemplate, Task<bool>> _delete;
    private string _search = string.Empty;
    private string _saveName = string.Empty;
    private IReadOnlyList<MailTemplate> _items;
    private string? _error;

    public TemplatePickerState(IReadOnlyList<MailTemplate> templates, Func<MailTemplate, Task> insert, Func<string, Task<MailTemplate?>> save, Func<MailTemplate, Task<bool>> delete)
    {
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(insert);
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(delete);
        _all = [.. templates];
        _insert = insert;
        _save = save;
        _delete = delete;
        _items = [.. _all];
    }

    /// <summary>The picker asked to close (a template was inserted, or Cancel).</summary>
    public event EventHandler? CloseRequested;

    public IReadOnlyList<MailTemplate> Items
    {
        get => _items;
        private set => SetProperty(ref _items, value);
    }

    /// <summary>Filters by name, subject and category, case-insensitively.</summary>
    public string Search
    {
        get => _search;
        set
        {
            if (SetProperty(ref _search, value ?? string.Empty))
            {
                Refilter();
            }
        }
    }

    /// <summary>The name for "Save as template".</summary>
    public string SaveName
    {
        get => _saveName;
        set => SetProperty(ref _saveName, value ?? string.Empty);
    }

    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    /// <summary>Inserts the template into the composer and closes.</summary>
    public async Task InsertAsync(MailTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        await _insert(template).ConfigureAwait(true);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Saves the message as a template under <see cref="SaveName"/>; the new template joins the list.</summary>
    public async Task SaveAsync()
    {
        var name = _saveName.Trim();
        if (name.Length == 0)
        {
            Error = "Give the template a name.";
            return;
        }

        var made = await _save(name).ConfigureAwait(true);
        if (made is null)
        {
            Error = "The template could not be saved.";
            return;
        }

        Error = null;
        SaveName = string.Empty;
        _all.Add(made);
        _all.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        Refilter();
    }

    /// <summary>Deletes a template (the store writes a tombstone).</summary>
    public async Task DeleteAsync(MailTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (await _delete(template).ConfigureAwait(true))
        {
            _ = _all.RemoveAll(t => string.Equals(t.Id, template.Id, StringComparison.Ordinal));
            Refilter();
        }
        else
        {
            Error = "The template could not be deleted.";
        }
    }

    public void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Refilter()
    {
        var q = _search.Trim();
        Items = q.Length == 0
            ? [.. _all]
            : [.. _all.Where(t => t.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || t.Subject.Contains(q, StringComparison.OrdinalIgnoreCase) || t.Category.Contains(q, StringComparison.OrdinalIgnoreCase))];
    }
}
