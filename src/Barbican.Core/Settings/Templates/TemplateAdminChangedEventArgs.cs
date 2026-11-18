// Mirrors: see ITemplateAdmin; owner srv-data.

namespace Barbican.Core.Settings.Templates;

/// <summary>The templates of an account changed.</summary>
public sealed class TemplateAdminChangedEventArgs(string accountKey) : EventArgs
{
    public string AccountKey { get; } = accountKey;
}
