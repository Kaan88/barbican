namespace Barbican.Spike.BodyShell;

/// <summary>What the scenario found; written as report.json next to the saved frames.</summary>
internal sealed class SpikeReport
{
    public string Platform { get; set; } = string.Empty;

    public string Head { get; set; } = string.Empty;

    public string Capture { get; set; } = string.Empty;

    public string StartedUtc { get; set; } = string.Empty;

    public double Scale { get; set; }

    public List<CheckResult> Checks { get; } = [];

    public List<PhaseResult> Phases { get; } = [];

    public List<string> Notes { get; } = [];
}
