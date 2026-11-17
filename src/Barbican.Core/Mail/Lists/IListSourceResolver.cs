// Mirrors: M2 plan section 4.4 (IListSourceResolver). Contract from stage F of M2; default: the active account. Owner unified (wave 2): __unified and shared sources.

using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Lists;

/// <summary>Chooses the source for a query.</summary>
public interface IListSourceResolver
{
    /// <summary>The source that answers the query.</summary>
    IListSource Resolve(ListQuery query);
}
