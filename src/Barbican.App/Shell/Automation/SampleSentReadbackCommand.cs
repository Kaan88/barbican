#if WINDOWS && AUTOMATION
using Barbican.Automation;
using Barbican.Core.Storage;

namespace Barbican.App.Shell.Automation;

internal sealed class SampleSentReadbackCommand(SampleActiveMail mail, IProfilePaths profile) : IAutomationCommand
{
    public string Name => "sample-sent-keywords";
    public Task<string> RunAsync(string args, CancellationToken cancellationToken)
    {
        if (!ProfilePaths.IsAutomationProfile(profile.Profile) || !Enabled("BARBICAN_SAMPLE") || !Enabled("BARBICAN_SAMPLE_ADDON_ACCOUNT") || !Enabled("BARBICAN_SAMPLE_SEND_PIPELINE")) return Task.FromResult("error sample send pipeline disabled");
        if (string.Equals(args.Trim(), "raw-state", StringComparison.Ordinal)) return Task.FromResult(mail.ReadRawState());
        if (string.IsNullOrWhiteSpace(args)) return Task.FromResult("error expected subject");
        return Task.FromResult(mail.ReadSentKeywords(args.Trim()));
    }
    private static bool Enabled(string name) => string.Equals(Environment.GetEnvironmentVariable(name), "1", StringComparison.Ordinal);
}
#endif
