// Mirrors: Bulwark components/templates/template-manager-modal.tsx and template-form.tsx (name, subject, body, rich or plain, delete), account-template-sync.tsx (the templates live in the Inbox metadata), Rust templates.rs; I-289, I-290. M6 plan section 4.8 (ITemplateAdmin, D-M6-C04).
// Placeholders are read from the body and subject; the body is plain text or HTML source (decision D-M6-D03: no WYSIWYG in the manager, the composer inserts rich templates). Export shows the document as text and import takes pasted text (no file dialog in the page). Owner srv-ui.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Settings.Templates;
using Barbican.Presentation.Common;

namespace Barbican.Presentation.Settings.Server;

/// <summary>The templates page: the list and the form of the selected template.</summary>
public sealed partial class SrvUiTemplatesViewModel : SrvUiPageModel
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    private readonly ITemplateAdmin _service;
    private IReadOnlyList<MailTemplate> _items = [];
    private string? _selectedId;
    private bool _creating;
    private string _message = string.Empty;

    public SrvUiTemplatesViewModel(ITemplateAdmin service, IAccountManager? accounts = null, IDispatcher? dispatcher = null)
        : base(accounts, dispatcher)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        _service.Changed += (_, e) =>
        {
            if (IsMine(e.AccountKey))
            {
                _ = ReloadQuietlyAsync();
            }
        };
    }

    public IReadOnlyList<MailTemplate> Items => _items;

    public string? SelectedId => _selectedId;

    public bool IsNew => _selectedId is null;

    public string Name { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public bool IsHtml { get; set; }

    /// <summary>The placeholders the subject and body use, each once ({{name}}).</summary>
    public IReadOnlyList<string> Placeholders => Find(Subject + "\n" + Body);

    public string Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    /// <summary>The export document as indented text (after <see cref="ExportAsync"/>).</summary>
    public string ExportText { get; private set; } = string.Empty;

    public async Task LoadAsync(CancellationToken ct)
    {
        IsLoading = true;
        Take(await _service.ListAsync(AccountKey, ct).ConfigureAwait(false));
        IsLoading = false;
    }

    public void StartNew()
    {
        _creating = true;
        Select(null);
    }

    public void Select(string? id)
    {
        _creating = id is null && _creating;
        _selectedId = id;
        var t = _items.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));
        Name = t?.Name ?? string.Empty;
        Subject = t?.Subject ?? string.Empty;
        Body = t?.Body ?? string.Empty;
        IsHtml = t?.IsHtml ?? false;
        Error = null;
        Raise(string.Empty);
    }

    public async Task SaveAsync(CancellationToken ct)
    {
        if (Name.Trim().Length == 0)
        {
            Error = "Give the template a name.";
            return;
        }

        var result = _selectedId is null
            ? await _service.CreateAsync(AccountKey, Name.Trim(), Subject, Body, IsHtml, ct).ConfigureAwait(false)
            : await _service.UpdateAsync(AccountKey, (_items.First(t => string.Equals(t.Id, _selectedId, StringComparison.Ordinal))) with { Name = Name.Trim(), Subject = Subject, Body = Body, IsHtml = IsHtml }, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            Error = result.Error!.Message;
            return;
        }

        Error = null;
        _creating = false;
        _selectedId = result.Value!.Id;
        Message = "Saved.";
        await LoadAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(CancellationToken ct)
    {
        if (_selectedId is null)
        {
            return;
        }

        var result = await _service.DeleteAsync(AccountKey, _selectedId, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            Error = result.Error!.Message;
            return;
        }

        _selectedId = null;
        await LoadAsync(ct).ConfigureAwait(false);
    }

    public async Task ExportAsync(CancellationToken ct)
    {
        var result = await _service.ExportAsync(AccountKey, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            Error = result.Error!.Message;
            return;
        }

        ExportText = result.Value!.ToJsonString(Pretty);
        Raise(nameof(ExportText));
    }

    /// <summary>Imports pasted text (a Bulwark export or a plain array of templates).</summary>
    public async Task ImportAsync(string text, CancellationToken ct)
    {
        JsonNode? doc;
        try
        {
            doc = JsonNode.Parse(text ?? string.Empty);
        }
        catch (JsonException)
        {
            doc = null;
        }

        if (doc is null)
        {
            Error = "That is not a template export.";
            return;
        }

        var result = await _service.ImportAsync(AccountKey, doc, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            Error = result.Error!.Message;
            return;
        }

        Error = null;
        Message = "Imported " + result.Value + " template" + (result.Value == 1 ? string.Empty : "s") + ".";
        await LoadAsync(ct).ConfigureAwait(false);
    }

    [GeneratedRegex(@"\{\{\s*(?<n>[A-Za-z0-9_\-]+)\s*\}\}", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 200)]
    private static partial Regex PlaceholderPattern();

    private static IReadOnlyList<string> Find(string text)
    {
        try
        {
            return TemplateAdminPlaceholders.Find(text);
        }
        catch (PortPendingException)
        {
            return [.. PlaceholderPattern().Matches(text).Select(m => m.Groups["n"].Value).Distinct(StringComparer.Ordinal)];
        }
    }

    private void Take(Result<IReadOnlyList<MailTemplate>> result)
    {
        if (!result.IsOk)
        {
            Error = result.Error!.Message;
            return;
        }

        Error = null;
        _items = result.Value!;
        if (_creating)
        {
            Raise(nameof(Items));
            return;
        }

        var keep = _selectedId is not null && _items.Any(i => string.Equals(i.Id, _selectedId, StringComparison.Ordinal)) ? _selectedId : _items.Count > 0 ? _items[0].Id : null;
        Select(keep);
    }

    private async Task ReloadQuietlyAsync()
    {
        try
        {
            Take(await _service.ListAsync(AccountKey, CancellationToken.None).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // the page went away
        }
    }
}
