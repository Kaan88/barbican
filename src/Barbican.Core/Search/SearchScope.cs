// Mirrors: Rust gsearch.rs:14 Scope; Bulwark lib/search-syntax.ts (in:mail, in:files, ...). Stub from stage F of M2; owner search.

namespace Barbican.Core.Search;

/// <summary>Which app a search covers.</summary>
public enum SearchScope
{
    All,
    Mail,
    Contacts,
    Calendar,
    Files,
}
