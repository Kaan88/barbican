// Mirrors: see IFolderAdmin; owner srv-data.

namespace Barbican.Core.Settings.Folders;

/// <summary>The folders or their preferences changed.</summary>
public sealed class FolderAdminChangedEventArgs(string accountKey) : EventArgs
{
    public string AccountKey { get; } = accountKey;
}
