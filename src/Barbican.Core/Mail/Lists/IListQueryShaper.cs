// Mirrors: M2 plan section 4.4 (IListQueryShaper). Contract from stage F of M2; default: the identity. Owner search (wave 2): gsearch filter, sort, collapseThreads.

using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Lists;

/// <summary>Adjusts the query a list is about to run (search filters, sort, thread collapse).</summary>
public interface IListQueryShaper
{
    /// <summary>The query to run; the default returns it unchanged.</summary>
    ListQuery Shape(ListQuery query);
}
