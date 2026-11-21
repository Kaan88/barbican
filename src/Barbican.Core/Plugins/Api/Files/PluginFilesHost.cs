// Mirrors: see IPluginFilesHost. The production host: the app's file store, writer and transfer service, and the shared per-add-on rate limiter.
using Barbican.Core.Files;
using Barbican.Core.Plugins.Api.Mail;

namespace Barbican.Core.Plugins.Api.Files;

/// <summary>The file services the Files view uses, handed to the add-on methods.</summary>
public sealed class PluginFilesHost(IFileStore store, IFileWriter writer, IFileTransfer transfer, IPluginMailRateLimiter limiter) : IPluginFilesHost
{
    public IFileStore Store { get; } = store ?? throw new ArgumentNullException(nameof(store));

    public IFileWriter Writer { get; } = writer ?? throw new ArgumentNullException(nameof(writer));

    public IFileTransfer Transfer { get; } = transfer ?? throw new ArgumentNullException(nameof(transfer));

    public IPluginMailRateLimiter Limiter { get; } = limiter ?? throw new ArgumentNullException(nameof(limiter));
}
