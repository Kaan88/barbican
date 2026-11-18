// Mirrors: M5 plan section 4.12 (the shell seam): registers the Files screen as the screen of the rail's Files app; built on the first visit, then only hidden. Available in a sample-data run (BARBICAN_SAMPLE=1), else when the active login advertises the filenode capability.

using Barbican.App.Shell;
using Barbican.Core.Accounts;
using Barbican.Presentation.Shell;

namespace Barbican.App.Views.Files;

/// <summary>Creates the Files screen.</summary>
public sealed class FilesScreenFactory : IAppScreenFactory
{
    /// <summary>The account key of a sample-data run.</summary>
    public const string SampleKey = "sample";

    private const string FileNodeCapability = "urn:ietf:params:jmap:filenode";
    private FilesScreen? _screen;

    public AppId App => AppId.Files;

    public bool IsAvailable()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("BARBICAN_SAMPLE"), "1", StringComparison.Ordinal))
        {
            return true;
        }

        var accounts = ViewHost.Find<IAccountManager>();
        return accounts?.Active is { } active && accounts.SessionFor(active.Key) is { } session && session.Client.Session.Has(FileNodeCapability);
    }

    public UIElement Create()
    {
        if (ViewHost.Find<Barbican.Presentation.Files.FilesPicker>() is { } picker)
        {
            picker.Platform ??= FilesSystemPicker.PickAsync;
        }

        return _screen ??= new FilesScreen();
    }

    public void OnShown() => _screen?.OnShown();
}
