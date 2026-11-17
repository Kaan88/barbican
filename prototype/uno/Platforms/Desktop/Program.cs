using Uno.UI.Hosting;

namespace Barbican;

internal static class Program
{
    [System.STAThread]
    public static void Main(string[] args)
    {
        UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .UseX11()
            .UseWin32()
            .Build()
            .Run();
    }
}
