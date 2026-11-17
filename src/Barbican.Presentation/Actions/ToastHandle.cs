// Mirrors: M2 plan section 4.3 (IToastService.Show returns a handle). Contract from stage F of M2; owner actions.

namespace Barbican.Presentation.Actions;

/// <summary>Identifies a shown toast so it can be dismissed.</summary>
public readonly record struct ToastHandle(long Id);
