// Mirrors: see IVacationService. Stub from the M6 plan section 4.7; owner srv-data replaces the class (constructor dependencies: IAccountManager, SieveScriptStore, IHookBus).

using Barbican.Core.Common;

namespace Barbican.Core.Settings.Vacation;

/// <summary>The default <see cref="IVacationService"/>.</summary>
public sealed class VacationService : IVacationService
{
    public event EventHandler<VacationChangedEventArgs>? Changed
    {
        add { }
        remove { }
    }

    public Task<Result<VacationSettings>> LoadAsync(string accountKey, CancellationToken ct) => throw new PortPendingException("VacationService.LoadAsync");

    public Task<Result<VacationSettings>> SaveAsync(string accountKey, VacationSettings settings, CancellationToken ct) => throw new PortPendingException("VacationService.SaveAsync");
}
