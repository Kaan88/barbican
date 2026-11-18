// Mirrors: see IVacationService; owner srv-data.

namespace Barbican.Core.Settings.Vacation;

/// <summary>The vacation setting of an account changed.</summary>
public sealed class VacationChangedEventArgs(string accountKey) : EventArgs
{
    public string AccountKey { get; } = accountKey;
}
