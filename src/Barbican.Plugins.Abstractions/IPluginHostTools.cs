// Mirrors: Rust agent.rs tool set and exec (B-AI-007, B-AI-008), generalized as narrow host services over the real mail, calendar, contact, filter and settings services (decision D-M6b-08).

namespace Barbican.Plugins;

/// <summary>The mail operations the host runs for an add-on, as JSON in and text out. Mail text comes back inside &lt;email&gt; tags: it is untrusted data (B-AI-004).</summary>
public interface IPluginHostTools
{
    /// <summary>The tools that apply to this account (calendar and contact tools only when the server has them).</summary>
    IReadOnlyList<HostToolSpec> Specs(string accountKey);

    /// <summary>Runs one tool; the result is text the add-on may hand to a model (a failure is text, not an exception).</summary>
    Task<string> RunAsync(string accountKey, string name, string argumentsJson, CancellationToken ct);
}
