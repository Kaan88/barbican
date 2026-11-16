using Uno.UI.Hosting;

namespace Barbican.Spike.BodyShell;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        UnoPlatformHostBuilder.Create()
            .App(() => new SpikeApp())
            .UseX11()
            .UseWin32()
            .Build()
            .Run();
    }
}
