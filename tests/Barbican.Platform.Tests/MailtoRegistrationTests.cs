#pragma warning disable MA0002, MA0006 // asserts compare strings ordinally by nature
using Barbican.Core.Mailto;
using Barbican.Platform.Mailto;

namespace Barbican.Platform.Tests;

/// <summary>The executors of the default-mail-app plans, run against fake doors only: nothing here touches the registry, the home folder or xdg-mime (CLAUDE.md: no OS changes without the user's OK).</summary>
public sealed class MailtoRegistrationTests
{
    private sealed class FakeRegistry : IRegistryWriter
    {
        public List<(string Key, string Name, string Value)> Set { get; } = [];

        public List<string> DeletedKeys { get; } = [];

        public List<(string Key, string Name)> DeletedValues { get; } = [];

        public void SetString(string key, string name, string value) => Set.Add((key, name, value));

        public void DeleteKey(string key) => DeletedKeys.Add(key);

        public void DeleteValue(string key, string name) => DeletedValues.Add((key, name));
    }

    private sealed class FakeFiles : IDesktopFiles
    {
        public string ApplicationsDir => "/home/u/.local/share/applications";

        public Dictionary<string, string> Written { get; } = [];

        public List<string> Deleted { get; } = [];

        public List<string> Ran { get; } = [];

        public bool XdgWorks { get; set; } = true;

        public void WriteFile(string path, string content) => Written[path] = content;

        public void DeleteFile(string path) => Deleted.Add(path);

        public bool Run(string program, IReadOnlyList<string> args)
        {
            Ran.Add(program + " " + string.Join(' ', args));
            return program != "xdg-mime" || XdgWorks;
        }
    }

    [Fact]
    public void The_windows_registration_writes_exactly_the_plan_and_only_under_HKCU()
    {
        var registry = new FakeRegistry();
        var message = new DefaultMailAppRegistrar(registry).Register(@"C:\Apps\Barbican\barbican.exe");
        Assert.Equal(DefaultMailApp.WindowsPlan(@"C:\Apps\Barbican\barbican.exe").Select(w => (w.Key, w.Name, w.Value)), registry.Set);
        Assert.All(registry.Set, w => Assert.StartsWith(@"HKCU\Software\", w.Key, StringComparison.Ordinal));
        Assert.Contains("registered", message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_windows_removal_deletes_the_keys_and_the_one_value_the_registration_wrote()
    {
        var registry = new FakeRegistry();
        _ = new DefaultMailAppRegistrar(registry).Unregister();
        Assert.Equal([@"HKCU\Software\Classes\Barbican.Url.mailto", @"HKCU\Software\Clients\Mail\Barbican"], registry.DeletedKeys);
        Assert.Equal([(@"HKCU\Software\RegisteredApplications", "Barbican")], registry.DeletedValues);
    }

    [Fact]
    public void The_desktop_registration_writes_the_entry_then_asks_xdg_mime()
    {
        var files = new FakeFiles();
        var registrar = new DesktopEntryRegistrar(files);
        _ = registrar.Register("/opt/barbican/barbican");
        var (path, content) = Assert.Single(files.Written);
        Assert.EndsWith("barbican.desktop", path, StringComparison.Ordinal);
        Assert.Contains("MimeType=x-scheme-handler/mailto;", content, StringComparison.Ordinal);
        Assert.Equal(["update-desktop-database /home/u/.local/share/applications", "xdg-mime default barbican.desktop x-scheme-handler/mailto"], files.Ran);
    }

    [Fact]
    public void A_failing_xdg_mime_is_reported_not_hidden()
    {
        var files = new FakeFiles { XdgWorks = false };
        _ = Assert.Throws<IOException>(() => new DesktopEntryRegistrar(files).Register("/opt/barbican/barbican"));
    }

    [Fact]
    public void The_desktop_removal_deletes_the_entry()
    {
        var files = new FakeFiles();
        _ = new DesktopEntryRegistrar(files).Unregister();
        Assert.EndsWith("barbican.desktop", Assert.Single(files.Deleted), StringComparison.Ordinal);
    }
}
