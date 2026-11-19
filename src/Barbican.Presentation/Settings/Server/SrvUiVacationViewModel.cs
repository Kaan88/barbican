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
    private bool _enabled;
    private string _fromText = string.Empty;
    private string _toText = string.Empty;
    private string _audienceOnly = string.Empty;
    private string _domainsText = string.Empty;
    private bool _forwardOn;
    private string _forwardTo = string.Empty;
    private bool _forwardKeep = true;
    private bool _blockCalendar;
    private long _draftVersion;
    private long _savedDraftVersion;
    private long _accountGeneration;
    private long _loadedGeneration = -1;
    private long _loadSequence;
    private string? _loadedAccount;

    public SrvUiVacationViewModel(IVacationService service, IAccountManager? accounts = null, IDispatcher? dispatcher = null, TimeProvider? time = null)
        : base(accounts, dispatcher)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        _time = time ?? TimeProvider.System;
        if (accounts is not null)
        {
            accounts.ActiveChanged += (_, _) => AccountChanged();
        }

        _service.Changed += (_, e) =>
        {
            _ = ReloadQuietlyAsync(e.AccountKey);
        };
    }

    public bool CanSave => IsLoaded(AccountKey, Interlocked.Read(ref _accountGeneration)) && !IsLoading;

    public bool Enabled { get => _enabled; set => Edit(ref _enabled, value); }

    public string Subject { get => _subject; set => Edit(ref _subject, value); }

    public string Body { get => _body; set => Edit(ref _body, value); }

    /// <summary>Optional HTML source; plain text remains independently editable.</summary>
    public string HtmlBody { get => _htmlBody; set => Edit(ref _htmlBody, value); }

    /// <summary>First day, yyyy-MM-dd, or empty.</summary>
    public string FromText { get => _fromText; set => Edit(ref _fromText, value); }

    /// <summary>Last day, yyyy-MM-dd, or empty.</summary>
    public string ToText { get => _toText; set => Edit(ref _toText, value); }

    /// <summary>"" (everyone), "internal" or "external".</summary>
    public string AudienceOnly { get => _audienceOnly; set => Edit(ref _audienceOnly, value); }

    /// <summary>Own domains, comma separated, for the audience choice.</summary>
    public string DomainsText { get => _domainsText; set => Edit(ref _domainsText, value); }

    public bool ForwardOn { get => _forwardOn; set => Edit(ref _forwardOn, value); }

    public string ForwardTo { get => _forwardTo; set => Edit(ref _forwardTo, value); }

    public bool ForwardKeep { get => _forwardKeep; set => Edit(ref _forwardKeep, value); }

    public bool BlockCalendar { get => _blockCalendar; set => Edit(ref _blockCalendar, value); }

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
        var generation = Interlocked.Read(ref _accountGeneration);
        var sequence = Interlocked.Increment(ref _loadSequence);
        var version = _draftVersion;
        var preserve = IsLoaded(account, generation) && version != _savedDraftVersion;
        var result = await _service.LoadAsync(account, ct).ConfigureAwait(false);
        await OnUiAsync(account, () =>
        {
            if (generation != Interlocked.Read(ref _accountGeneration) || sequence != Interlocked.Read(ref _loadSequence)) return;
            Take(result, preserve || version != _draftVersion);
            if (result.IsOk)
            {
                _loadedAccount = account;
                _loadedGeneration = generation;
            }

            IsLoading = false;
            Raise(nameof(CanSave));
        }).ConfigureAwait(false);
    }

    public async Task SaveAsync(CancellationToken ct)
    {
        Message = string.Empty;
        var account = AccountKey;
        var generation = Interlocked.Read(ref _accountGeneration);
        if (!IsLoaded(account, generation) || IsLoading)
        {
            Error = "Wait for this account's out-of-office settings to load.";
            return;
        }

        if (!TryDay(FromText, out var from) || !TryDay(ToText, out var to))
        {
            Error = "Enter days as yyyy-MM-dd.";
            return;
        }

        var domains = DomainsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var audience = AudienceOnly.Length == 0 ? null : new VacationAudience(AudienceOnly, domains);
        var forward = ForwardOn || ForwardTo.Trim().Length > 0 ? new VacationForward(ForwardOn, ForwardTo.Trim(), ForwardKeep) : null;
        var settings = _saved! with
        {
            Enabled = Enabled,
            Subject = Subject.Trim(),
            TextBody = Body,
            HtmlBody = string.IsNullOrWhiteSpace(HtmlBody) ? null : HtmlBody,
            From = from,
            To = to,
            Audience = audience,
            Forward = forward,
            BlockCalendar = BlockCalendar,
        };
        var version = _draftVersion;
        var loadSequence = Interlocked.Read(ref _loadSequence);
        IsLoading = true;
        var result = await _service.SaveAsync(account, settings, ct).ConfigureAwait(false);
        await OnUiAsync(account, () =>
        {
            if (generation != Interlocked.Read(ref _accountGeneration) || loadSequence != Interlocked.Read(ref _loadSequence)) return;
            IsLoading = false;
            var newerEdits = version != _draftVersion;
            Take(result, newerEdits);
            if (result.IsOk)
            {
                Message = newerEdits ? "Saved earlier changes. Newer edits are not saved yet." : "Saved.";
            }
        }).ConfigureAwait(false);
    }

    private bool IsLoaded(string account, long generation) =>
        generation == _loadedGeneration && string.Equals(account, _loadedAccount, StringComparison.Ordinal);

    private void AccountChanged()
    {
        var generation = Interlocked.Increment(ref _accountGeneration);
        _ = Interlocked.Increment(ref _loadSequence);
        var account = AccountKey;
        _ = OnUiAsync(account, () =>
        {
            if (generation != Interlocked.Read(ref _accountGeneration)) return;
            _loadedAccount = null;
            _loadedGeneration = -1;
            _saved = null;
            _enabled = false;
            _subject = string.Empty;
            _body = string.Empty;
            _htmlBody = string.Empty;
            _fromText = string.Empty;
            _toText = string.Empty;
            _audienceOnly = string.Empty;
            _domainsText = string.Empty;
            _forwardOn = false;
            _forwardTo = string.Empty;
            _forwardKeep = true;
            _blockCalendar = false;
            _draftVersion++;
            _savedDraftVersion = _draftVersion;
            Error = null;
            Message = string.Empty;
            IsLoading = false;
            Raise(nameof(Status));
            Raise(string.Empty);
            Raise(nameof(CanSave));
        });
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

    private void Edit<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
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

        _enabled = v.Enabled;
        _subject = v.Subject;
        _body = v.TextBody;
        _htmlBody = v.HtmlBody ?? string.Empty;
        _savedDraftVersion = _draftVersion;
        _fromText = Day(v.From);
        _toText = Day(v.To);
        _audienceOnly = v.Audience?.Only ?? string.Empty;
        _domainsText = string.Join(", ", v.Audience?.Domains ?? []);
        _forwardOn = v.Forward?.Enabled ?? false;
        _forwardTo = v.Forward?.To ?? string.Empty;
        _forwardKeep = v.Forward?.KeepCopy ?? true;
        _blockCalendar = v.BlockCalendar;
        Raise(nameof(Status));
        Raise(string.Empty);
    }

    private async Task ReloadQuietlyAsync(string account)
    {
        try
        {
            var generation = Interlocked.Read(ref _accountGeneration);
            var result = await _service.LoadAsync(account, CancellationToken.None).ConfigureAwait(false);
            await OnUiAsync(account, () =>
            {
                if (generation == Interlocked.Read(ref _accountGeneration) && IsLoaded(account, generation) && !IsLoading && _draftVersion == _savedDraftVersion) Take(result);
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // the page went away
        }
    }
}
