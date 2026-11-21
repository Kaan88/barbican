// The M5 merges collided on names (two streams made CalendarRig, two made a public ICalendarSource): every public type in a namespace an M6 stream owns carries that stream's prefix (the interface's leading I aside), and no such type shares a simple name with any other public type of Core or Presentation.
// The table is M6 plan section 3.3; a stream asks the integrator before it adds a namespace. Green in the plan commit. Owner integrator.

using Barbican.Core.Settings.Pages;
using Barbican.Presentation.Settings;

namespace Barbican.Presentation.Tests.Settings;

public sealed class M6NamingTests
{
    private static readonly (string Namespace, string Stream, string[] Prefixes)[] Owned =
    [
        ("Barbican.Core.Settings.Pages", "set-data", ["Setting"]),
        ("Barbican.Core.Settings.Backup", "set-data", ["Setting"]),
        ("Barbican.Core.Settings.Account", "set-data", ["AccountIdentit", "AccountSecurity", "AccountQuota", "AccountAppPassword", "AccountApiKey", "AccountIssuedSecret", "AccountTotp"]),
        ("Barbican.Core.Filters.Rules", "srv-data", ["Filter"]),
        ("Barbican.Core.Settings.Vacation", "srv-data", ["Vacation"]),
        ("Barbican.Core.Settings.Folders", "srv-data", ["FolderAdmin"]),
        ("Barbican.Core.Settings.Templates", "srv-data", ["TemplateAdmin"]),
        ("Barbican.Presentation.Settings.Ui", "set-ui", ["SetUi"]),
        ("Barbican.Presentation.Settings.Server", "srv-ui", ["SrvUi"]),
    ];

    private static IEnumerable<Type> Public()
    {
        foreach (var assembly in new[] { typeof(SettingRow).Assembly, typeof(SettingsNav).Assembly })
        {
            foreach (var type in assembly.GetExportedTypes().Where(t => !t.IsNested))
            {
                yield return type;
            }
        }
    }

    private static string Simple(Type type)
    {
        var name = type.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        return tick >= 0 ? name[..tick] : name;
    }

    private static string Bare(Type type)
    {
        var name = Simple(type);
        return type.IsInterface && name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]) ? name[1..] : name;
    }

    [Fact]
    public void Every_type_of_an_owned_namespace_has_its_streams_prefix()
    {
        var wrong = new List<string>();
        foreach (var type in Public())
        {
            foreach (var (ns, stream, prefixes) in Owned)
            {
                if (string.Equals(type.Namespace, ns, StringComparison.Ordinal) && !prefixes.Any(p => Bare(type).StartsWith(p, StringComparison.Ordinal)))
                {
                    wrong.Add($"{type.FullName} ({stream}: {string.Join(" or ", prefixes)})");
                }
            }
        }

        Assert.True(wrong.Count == 0, "types without their stream's prefix: " + string.Join("; ", wrong));
    }

    [Fact]
    public void No_type_of_an_owned_namespace_shares_its_name_with_another_public_type()
    {
        var all = Public().ToList();
        var clash = new List<string>();
        foreach (var type in all.Where(t => Owned.Any(o => string.Equals(o.Namespace, t.Namespace, StringComparison.Ordinal))))
        {
            var others = all.Where(o => o != type && string.Equals(Simple(o), Simple(type), StringComparison.Ordinal)).Select(o => o.FullName).ToList();
            if (others.Count > 0)
            {
                clash.Add($"{type.FullName} also {string.Join(", ", others)}");
            }
        }

        Assert.True(clash.Count == 0, "name clashes: " + string.Join("; ", clash));
    }
}
