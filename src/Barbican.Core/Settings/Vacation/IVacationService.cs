// Mirrors: see VacationSettings. Where it is stored (D-M6-C02, Bulwark vacation-store.ts): the plain fields go to VacationResponse when the server advertises urn:ietf:params:jmap:vacationresponse; the extras (audience, forward, calendar block, HTML body kept in the script's metadata) force the Sieve metadata path (`includeVacation`, `vacation`, `vacationAudience`, `vacationForward`, B-SIEVE-002, B-SIEVE-012).
// Hooks onVacationLoaded, onBeforeVacationUpdate (intercept), onVacationUpdate. THREADING (D-M6-00): completes off the UI thread; Changed is raised on a pool thread. Contract from the M6 plan section 4.7; owner srv-data.

using Barbican.Core.Common;

namespace Barbican.Core.Settings.Vacation;

/// <summary>The out-of-office setting of an account.</summary>
public interface IVacationService
{
    Task<Result<VacationSettings>> LoadAsync(string accountKey, CancellationToken ct);

    /// <summary>Saves (refused with <see cref="VacationRules.Problem"/> first) and returns what the server now has.</summary>
    Task<Result<VacationSettings>> SaveAsync(string accountKey, VacationSettings settings, CancellationToken ct);

    event EventHandler<VacationChangedEventArgs>? Changed;
}
