// panes.json in the Rust format (dnd.rs:256-264): {"sidebar": n, "list": n}, rounded, clamped on load and save, broken or missing file = defaults.

using Barbican.Core.Layout;

namespace Barbican.Core.Tests.Layout;

public sealed class PanesFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "barbican-panes-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Save_then_load_round_trips_in_the_rust_format()
    {
        var path = Path.Combine(_dir, PanesFile.FileName);
        PanesFile.Save(path, 220.4f, 360.6f);
        Assert.Equal("""{"sidebar":220,"list":361}""", File.ReadAllText(path));
        Assert.Equal((220f, 361f), PanesFile.Load(path));
    }

    [Fact]
    public void Values_are_clamped_and_junk_is_the_default()
    {
        var path = Path.Combine(_dir, PanesFile.FileName);
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, """{"sidebar": 50, "list": 9000}""");
        Assert.Equal((PaneLimits.SideMin, PaneLimits.ListMax), PanesFile.Load(path));
        File.WriteAllText(path, "not json");
        Assert.Equal((0f, 0f), PanesFile.Load(path));
        Assert.Equal((0f, 0f), PanesFile.Load(Path.Combine(_dir, "missing.json")));
    }
}
