// Mirrors: M5 plan section 4.10 (DI). Owner files-data: the store, writer and transfer. Defaults use TryAddSingleton.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.Core.Files;

/// <summary>Registers the files data layer (M5 stream files-data).</summary>
public static class FilesDataRegistration
{
    public static IServiceCollection AddBarbicanFilesData(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IFileStore, FileStore>();
        services.TryAddSingleton<IFileWriter, FileWriter>();
        services.TryAddSingleton<IFileTransfer, FileTransfer>();
        return services;
    }
}
