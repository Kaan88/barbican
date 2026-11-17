// Fable review 3 R3-04; docs/milestones/M2.md default 7: user plugins in <config>/plugins stay off until the M6b permission UI, only the bundled
// spam score is on. The app builds its host options through NativePluginHostOptions.ForApp (UnifiedRegistration.AddPlugins); a library put
// into <config>/plugins/x is not loaded. The control case loads the same folder when a user root is given, so the test cannot pass by a
// folder that would not load anyway. Owner unified.

using Barbican.Core.Plugins.Native;

namespace Barbican.Core.Tests.Plugins;

public sealed class UserPluginsOffTests
{
    [Fact]
    public void A_library_in_the_config_plugins_folder_is_not_loaded()
    {
        var root = Path.Combine(Path.GetTempPath(), "barbican-plugins-" + Guid.NewGuid().ToString("N"));
        try
        {
            var config = Path.Combine(root, "config");
            var userFolder = Path.Combine(config, "plugins", "x");
            CopyDirectory(Path.Combine(AppContext.BaseDirectory, "plugins", "spam-score"), userFolder);
            var appBase = Path.Combine(root, "app");
            Directory.CreateDirectory(appBase);

            var options = NativePluginHostOptions.ForApp(appBase, Path.Combine(root, "data"));
            Assert.Null(options.UserRoot);
            using var host = NativePluginHost.Load(options);
            Assert.Empty(host.Plugins);

            // control: the same folder does load when a user root is named
            using var control = NativePluginHost.Load(options with { UserRoot = Path.Combine(config, "plugins") });
            Assert.Contains(control.Plugins, p => string.Equals(p.Id, "spam-score", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                try
                {
                    Directory.Delete(root, recursive: true);
                }
                catch (UnauthorizedAccessException)
                {
                    // the control host keeps the plugin's library mapped until its context is collected
                }
                catch (IOException)
                {
                    // as above
                }
            }
        }
    }

    [Fact]
    public void The_app_roots_are_the_bundled_folder_and_the_data_folder()
    {
        var options = NativePluginHostOptions.ForApp(Path.Combine("app"), Path.Combine("data"));
        Assert.Equal(Path.Combine("app", "plugins"), options.BundledRoot);
        Assert.Equal(Path.Combine("data", "plugin-data"), options.DataRoot);
        Assert.Null(options.UserRoot);
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
