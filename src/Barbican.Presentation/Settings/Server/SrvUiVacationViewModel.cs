// Mirrors: Bulwark components/settings/vacation-settings.tsx (status line, dates, subject and body, audience, forward while away, calendar block), Rust server_settings.rs vacation; I-435..I-437, G-022. M6 plan section 4.7 (IVacationService, D-M6-C02).
// The draft is edited as text and checked when saved: a bad day is refused here, everything else by the service (VacationRules.Problem). Owner srv-ui.

using System.Globalization;
using Barbican.Core.Accounts;
using Barbican.Core.Settings.Vacation;
using Barbican.Presentation.Common;

namespace Barbican.Presentation.Settings.Server;

/// <summary>The out-of-office page.</summary>
public sealed class SrvUiVacationViewModel : SrvUiPageModel
{
    private readonly IVacationService _service;
    private readonly TimeProvider _time;
    private VacationSettings? _saved;
    private string _message = string.Empty;
    private string _subject = string.Empty;
    private string _body = string.Empty;
    private string _htmlBody = string.Empty;
    private long _draftVersion;
    private long _savedDraftVersion;

    public SrvUiVacationViewModel(IVacationService service, IAccountManager? accounts = null, IDispatcher? dispatcher = null, TimeProvider? time = null)
        : base(accounts, dispatcher)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        _time = time ?? TimeProvider.System;
        _service.Changed += (_, e) =>
        {
            _ = ReloadQuietlyAsync(e.AccountKey);
        };
    }

    public bool Enabled { get; set; }

    public string Subject { get => _subject; set => Edit(ref _subject, value); }

    public string Body { get => _body; set => Edit(ref _body, value); }

    /// <summary>Optional HTML source; plain text remains independently editable.</summary>
    public string HtmlBody { get => _htmlBody; set => Edit(ref _htmlBody, value); }

    /// <summary>First day, yyyy-MM-dd, or empty.</summary>
    public string FromText { get; set; } = string.Empty;

    /// <summary>Last day, yyyy-MM-dd, or empty.</summary>
    public string ToText { get; set; } = string.Empty;

    /// <summary>"" (everyone), "internal" or "external".</summary>
    public string AudienceOnly { get; set; } = string.Empty;

    /// <summary>Own domains, comma separated, for the audience choice.</summary>
    public string DomainsText { get; set; } = string.Empty;

    public bool ForwardOn { get; set; }

    public string ForwardTo { get; set; } = string.Empty;

    public bool ForwardKeep { get; set; } = true;

    public bool BlockCalendar { get; set; }

    /// <summary>"Off", "Active", "Active until ...", "Starts ...", "Ended ..." of what the server has.</summary>
    public string Status => _saved is null ? string.Empty : SrvUiVacationRules.Status(_saved, DateOnly.FromDateTime(_time.GetLocalNow().DateTime));

    /// <summary>"Saved." after a save.</summary>
    public string Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        IsLoading = true;
        var account = AccountKey;
        var version = _draftVersion;
        var result = await _service.LoadAsync(account, ct).ConfigureAwait(false);
        await OnUiAsync(account, () => { Take(result, version != _draftVersion); IsLoading = false; }).ConfigureAwait(false);
    }

    public async Task SaveAsync(CancellationToken ct)
    {
        Message = string.Empty;
        if (!TryDay(FromText, out var from) || !TryDay(ToText, out var to))
        {
            Error = "Enter days as yyyy-MM-dd.";
            return;
        }

        var domains = DomainsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var audience = AudienceOnly.Length == 0 ? null : new VacationAudience(AudienceOnly, domains);
        var forward = ForwardOn || ForwardTo.Trim().Length > 0 ? new VacationForward(ForwardOn, ForwardTo.Trim(), ForwardKeep) : null;
        var settings = new VacationSettings(Enabled, Subject.Trim(), Body, string.IsNullOrWhiteSpace(HtmlBody) ? null : HtmlBody, from, to, audience, forward, BlockCalendar);
        IsLoading = true;
        var account = AccountKey;
        var version = _draftVersion;
        var result = await _service.SaveAsync(account, settings, ct).ConfigureAwait(false);
        await OnUiAsync(account, () =>
        {
            IsLoading = false;
            var newerEdits = version != _draftVersion;
            Take(result, newerEdits);
            if (result.IsOk)
            {
                Message = newerEdits ? "Saved earlier changes. Newer edits are not saved yet." : "Saved.";
            }
        }).ConfigureAwait(false);
    }

    private static bool TryDay(string text, out DateOnly? day)
    {
        day = null;
        if (text.Trim().Length == 0)
        {
            return true;
        }

        if (DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            day = d;
            return true;
        }

        return false;
    }

    private static string Day(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    private void Edit(ref string field, string value)
    {
        if (string.Equals(field, value, StringComparison.Ordinal)) return;
        field = value;
        _draftVersion++;
    }

    private void Take(Barbican.Core.Common.Result<VacationSettings> result, bool preserveDraft = false)
    {
        if (!result.IsOk)
        {
            Error = result.Error!.Message;
            return;
        }

        Error = null;
        var v = result.Value!;
        _saved = v;
        if (preserveDraft)
        {
            Raise(nameof(Status));
            return;
        }

        Enabled = v.Enabled;
        _subject = v.Subject;
        _body = v.TextBody;
        _htmlBody = v.HtmlBody ?? string.Empty;
        _savedDraftVersion = _draftVersion;
        FromText = Day(v.From);
        ToText = Day(v.To);
        AudienceOnly = v.Audience?.Only ?? string.Empty;
        DomainsText = string.Join(", ", v.Audience?.Domains ?? []);
        ForwardOn = v.Forward?.Enabled ?? false;
        ForwardTo = v.Forward?.To ?? string.Empty;
        ForwardKeep = v.Forward?.KeepCopy ?? true;
        BlockCalendar = v.BlockCalendar;
        Raise(nameof(Status));
        Raise(string.Empty);
    }

    private async Task ReloadQuietlyAsync(string account)
    {
        try
        {
            var result = await _service.LoadAsync(account, CancellationToken.None).ConfigureAwait(false);
            await OnUiAsync(account, () => { if (_draftVersion == _savedDraftVersion) Take(result); }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // the page went away
        }
    }
}
