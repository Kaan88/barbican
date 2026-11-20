#if AUTOMATION && WINDOWS
using System.Text.Json;
using Barbican.App.Plugins.Sandbox;
using Barbican.App.Shell.Automation;
using Barbican.Automation;
using Barbican.Core.Accounts;
using Barbican.Core.OrganizationPolicy;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Storage;

namespace Barbican.App.Views.Compose.Automation;

/// <summary>Counts actual post writes and can revoke the synthetic provider after a checker request is queued.</summary>
internal sealed class ProofreadingProbeCommand : IAutomationCommand
{
    private readonly IAccountSessionSnapshots _accounts;
    private readonly OrganizationPolicyService _policy;
    private readonly bool _enabled;
    private AccountSessionSnapshot? _withdraw;
    private int _queued;
    private int _posts;
    private int _revoked;
    private bool _containsWitness;
    private bool _opaqueAlias;
    private bool _hasFixtureWords;
    private string[] _fields = [];

    public ProofreadingProbeCommand(IProfilePaths paths, IAccountSessionSnapshots accounts, OrganizationPolicyService policy)
    {
        _accounts = accounts;
        _policy = policy;
        _enabled = SamplePolicyFixture.Enabled(paths) &&
            string.Equals(Environment.GetEnvironmentVariable("BARBICAN_SAMPLE_PROOFREADING"), "1", StringComparison.Ordinal);
        if (!_enabled) return;
        PluginSandboxSession.BeforeSyntheticCheckerPost = Queued;
        PluginSandboxSession.AfterSyntheticCheckerPost = Posted;
    }

    public string Name => "proofreading-probe";

    public Task<string> RunAsync(string args, CancellationToken cancellationToken)
    {
        if (!_enabled) return Task.FromResult("error proofreading fixture is not enabled");
        if (string.Equals(args.Trim(), "revoke-next", StringComparison.Ordinal))
        {
            var selected = _accounts.CaptureActiveSession().Value;
            if (selected is null || _policy.ProviderFor(selected) is null) return Task.FromResult("error no active synthetic provider");
            _withdraw = selected;
        }
        else if (!string.IsNullOrWhiteSpace(args)) return Task.FromResult("error expected revoke-next or no argument");
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            queued = _queued, posted = _posts, revoked = _revoked, opaqueAlias = _opaqueAlias, hasFixtureWords = _hasFixtureWords,
            containsWitness = _containsWitness, fields = _fields,
        }));
    }

    private void Queued()
    {
        _queued++;
        var selected = _withdraw;
        _withdraw = null;
        if (selected is not null && _policy.RemoveProvider(selected)) _revoked++;
    }

    private void Posted(PluginValue arguments)
    {
        _posts++;
        var request = arguments.Item(0);
        _fields = [.. request.PropertyNames.Order(StringComparer.Ordinal)];
        _opaqueAlias = request.Property("composerId").StringValue.StartsWith("chk-", StringComparison.Ordinal);
        var segments = request.Property("segments");
        for (var index = 0; index < segments.Count; index++)
        {
            var text = segments.Item(index).Property("text").StringValue;
            _hasFixtureWords |= text.Contains("teh", StringComparison.Ordinal) || text.Contains("dem Brief", StringComparison.Ordinal);
            _containsWitness |= text.Contains("PROTECTED-WITNESS", StringComparison.Ordinal);
        }
    }
}
#endif
