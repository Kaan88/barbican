// Mirrors: Rust templates.rs (list, create, delete), Bulwark components/email/template-picker.tsx (search over name, subject and category; insert; save as template), lib/template-utils.ts (placeholders {{name}} / {{name:default}}, the auto-filled
// date and sender name, filterTemplates, validateTemplateName), stores/template-store.ts. I-255, I-256 (templates: insert, `t` key, save as template, placeholder fill, picker search). Owner send-pipeline; the picker and the fill dialog are composer-ui's views.

using System.ComponentModel;
using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Editor;

namespace Barbican.Presentation.Compose.Pipeline;

/// <summary>The template picker's data: the list (filtered by a search text), insert with the placeholders filled, save as template and delete.</summary>
public sealed class TemplatePickerViewModel(ITemplateStore store, IDocCodec codec, TimeProvider time, Barbican.Core.Storage.IPrefsStore? prefs = null) : INotifyPropertyChanged
{
    private IReadOnlyList<MailTemplate> _all = [];
    private IReadOnlyList<MailTemplate> _items = [];
    private string _search = "";
    private string _error = "";
    private string _accountId = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The sender's name for <c>{{sender_name}}</c> (the From identity's).</summary>
    public string SenderName { get; set; } = "";

    /// <summary>The templates that match <see cref="SearchText"/>, sorted by name.</summary>
    public IReadOnlyList<MailTemplate> Items
    {
        get => _items;
        private set
        {
            _items = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Items)));
        }
    }

    /// <summary>The picker's search box: a template shows when the text is in its name, subject or category.</summary>
    public string SearchText
    {
        get => _search;
        set
        {
            if (!string.Equals(_search, value, StringComparison.Ordinal))
            {
                _search = value;
                Items = TemplateText.Filter(_all, value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SearchText)));
            }
        }
    }

    /// <summary>The last failure in words; empty when all is well.</summary>
    public string Error
    {
        get => _error;
        private set
        {
            _error = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Error)));
        }
    }

    /// <summary>True when the server keeps no templates (the picker says so instead of listing nothing).</summary>
    public async Task<bool> LoadAsync(string accountId, CancellationToken ct)
    {
        _accountId = accountId;
        var r = await store.ListAsync(accountId, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            Error = r.Error!.Message;
            return false;
        }

        Error = "";
        _all = r.Value!;
        Items = TemplateText.Filter(_all, _search);
        return true;
    }

    /// <summary>The placeholders the dialog has to ask for: those in the template that nothing fills on its own (the date, the sender), with the default each carries (empty when none).</summary>
    public IReadOnlyList<(string Name, string Default)> PlaceholdersToAsk(MailTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var auto = TemplateText.AutoFilled(TimeZoneInfo.ConvertTime(time.GetUtcNow(), Barbican.Core.Settings.Pages.SettingDisplayTimeZone.Resolve(prefs?.Current.Str("timeZone", "auto"), time.LocalTimeZone)), SenderName, null);
        var defaults = TemplateText.Defaults(template);
        return [.. TemplateText.Placeholders(template).Where(n => !auto.ContainsKey(n)).Select(n => (n, defaults.TryGetValue(n, out var d) ? d : ""))];
    }

    /// <summary>The blocks and subject for <paramref name="template"/>: the auto-filled values, then the template's defaults, then what the user typed (a blank answer keeps the default); a placeholder nobody fills stays as it is, and the send warnings catch it.</summary>
    public TemplateInsertion Insert(MailTemplate template, IReadOnlyDictionary<string, string>? values = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        var merged = new Dictionary<string, string>(TemplateText.AutoFilled(TimeZoneInfo.ConvertTime(time.GetUtcNow(), Barbican.Core.Settings.Pages.SettingDisplayTimeZone.Resolve(prefs?.Current.Str("timeZone", "auto"), time.LocalTimeZone)), SenderName, null), StringComparer.Ordinal);
        foreach (var (k, v) in TemplateText.Defaults(template))
        {
            merged[k] = v;
        }

        foreach (var (k, v) in values ?? new Dictionary<string, string>(StringComparer.Ordinal))
        {
            if (v.Length > 0)
            {
                merged[k] = v;
            }
        }

        var body = TemplateText.Substitute(template.Body, merged, template.IsHtml);
        var subject = TemplateText.Substitute(template.Subject, merged);
        var blocks = template.IsHtml ? codec.Import(body) : codec.ImportText(body);
        return new TemplateInsertion(blocks, subject.Trim().Length == 0 ? null : subject);
    }

    /// <summary>"Save as template": the composer's body as HTML (or as text in a plain message) under <paramref name="name"/>.</summary>
    public async Task<Result<MailTemplate>> SaveAsync(string name, string subject, Doc body, bool plainText, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (TemplateText.ValidateName(name) is { } problem)
        {
            Error = string.Equals(problem, "empty", StringComparison.Ordinal) ? "Give the template a name." : $"The name is longer than {TemplateText.MaxNameLength} characters.";
            return Result<MailTemplate>.Fail(new Barbican.Core.Jmap.Errors.ValidationError(Error));
        }

        var r = await store.CreateAsync(_accountId, name.Trim(), subject, plainText ? codec.ToText(body) : codec.Export(body), !plainText, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            Error = r.Error!.Message;
            return r;
        }

        Error = "";
        _all = [.. _all.Append(r.Value!).OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)];
        Items = TemplateText.Filter(_all, _search);
        return r;
    }

    public async Task<Result<Unit>> DeleteAsync(MailTemplate template, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(template);
        var r = await store.DeleteAsync(_accountId, template.Id, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            Error = r.Error!.Message;
            return r;
        }

        Error = "";
        _all = [.. _all.Where(t => !string.Equals(t.Id, template.Id, StringComparison.Ordinal))];
        Items = TemplateText.Filter(_all, _search);
        return r;
    }
}
