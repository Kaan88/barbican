using Barbican.Core.Settings.Folders;

namespace Barbican.Presentation.Settings.Server;

/// <summary>An account-scoped folder and its display state.</summary>
public sealed record SrvUiFolderSnapshot(FolderAdminNode Node, int Unread, bool Subscribed);
