// Mirrors: M6 plan section 3.5 (sample-data runs): the ISettingInfo of a hidden sample run with texts that read like the real ones (the shared SampleSettingInfo answers "Sample S-keyless-NNN" for every row). Never registered outside BARBICAN_SAMPLE=1. Owner set-ui.

using Barbican.Core.Settings.Pages;
using Barbican.Presentation.Settings.Sample;

namespace Barbican.Presentation.Settings.Ui;

/// <summary>An <see cref="ISettingInfo"/> with sample texts by label.</summary>
public sealed class SetUiSampleInfo : ISettingInfo
{
    private static readonly Dictionary<string, string> Texts = new(StringComparer.Ordinal)
    {
        ["Email address"] = "sample@example.org",
        ["Username"] = "sample@example.org",
        ["Authentication"] = "Password",
        ["JMAP server"] = "mail.example.org",
        ["Storage usage"] = "3.5 MB of 1 GB used",
        ["Account type"] = "Personal",
        ["Your addresses"] = "sample@example.org",
        ["Version"] = "Barbican 0.0 (sample)",
    };

    private readonly Lazy<Dictionary<string, string>> _labels = new(() => SetUiCatalog.Load().SelectMany(p => p.Rows).Where(r => r.Key is null).ToDictionary(r => r.Id, r => r.Label, StringComparer.Ordinal));

    public bool Provides(string rowId) => _labels.Value.ContainsKey(rowId);

    public Task<string> TextAsync(string rowId, CancellationToken ct) => SampleThreading.OffThreadAsync(() => _labels.Value.TryGetValue(rowId, out var label) && Texts.TryGetValue(label, out var text) ? text : string.Empty);
}
