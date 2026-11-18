// Mirrors: M6 plan D-M6-A02 (one settings writer) and Rust settings.rs import/export/reset.
using System.Text.Json.Nodes;

namespace Barbican.Core.Settings.Prefs;

/// <summary>Serializes settings changes and backup snapshots within the running profile.</summary>
public sealed class SettingWriteGate : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    internal event EventHandler<SettingDesktopChangedEventArgs>? DesktopChanged;

    public void Dispose() => _semaphore.Dispose();

    internal Task WaitAsync(CancellationToken ct) => _semaphore.WaitAsync(ct);

    internal void PublishDesktop(JsonObject desktop) => DesktopChanged?.Invoke(this, new SettingDesktopChangedEventArgs(desktop));

    internal void Release() => _semaphore.Release();
}
