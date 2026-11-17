// Mirrors: M2 plan section 4.4 (the default of IListQueryShaper: behaviour unchanged from M1). Stage F of M2.

using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Lists;

/// <summary>Leaves the query as it is.</summary>
public sealed class IdentityListQueryShaper : IListQueryShaper
{
    /// <inheritdoc />
    public ListQuery Shape(ListQuery query) => query;
}
