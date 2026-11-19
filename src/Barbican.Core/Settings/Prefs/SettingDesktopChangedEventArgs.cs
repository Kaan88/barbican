// Carries the desktop snapshot between serialized settings writers.
using System.Text.Json.Nodes;

namespace Barbican.Core.Settings.Prefs;

internal sealed class SettingDesktopChangedEventArgs(JsonObject desktop) : EventArgs
{
    internal JsonObject Desktop { get; } = desktop;
}
