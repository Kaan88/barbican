// Mirrors: Bulwark components/settings/time-zone-picker.tsx and lib/time-zone-choices.ts.
using Barbican.App.Views.Dialogs;
using Barbican.Core.Calendar.Recurrence;
using Barbican.Core.Settings.Pages;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Settings;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Settings;

/// <summary>Searchable, virtualized selector inside the app's dialog host.</summary>
public sealed class TimeZonePickerContent : IDialogContent
{
    private readonly TimeProvider _time;

    public TimeZonePickerContent(TimeProvider time) => _time = time ?? throw new ArgumentNullException(nameof(time));

    private static readonly string[] Common = ["UTC", "America/Los_Angeles", "America/Denver", "America/Chicago", "America/New_York", "America/Sao_Paulo", "Europe/London", "Europe/Paris", "Europe/Berlin", "Europe/Moscow", "Asia/Dubai", "Asia/Kolkata", "Asia/Shanghai", "Asia/Tokyo", "Australia/Sydney"];
    private sealed record Choice(string Id, string Label, string Search, int Rank, int Offset);
    public bool OwnButtons => true;
    public double MaxWidth => 520;
    public bool CanDraw(object request) => request is TimeZonePickerRequest;

    public UIElement Create(ActiveDialog dialog)
    {
        var request = (TimeZonePickerRequest)dialog.Request;
        var panel = new StackPanel { Spacing = 10, MinWidth = 320 };
        var search = new TextBox { PlaceholderText = "Search city, zone or UTC offset" };
        AutomationProperties.SetAutomationId(search, "timezone.search");
        AutomationProperties.SetName(search, "Search time zones");
        var list = new ListView { Height = 360, SelectionMode = ListViewSelectionMode.Single };
        AutomationProperties.SetAutomationId(list, "timezone.list");
        AutomationProperties.SetName(list, "Time zones");
        var status = SetUiUi.Meta("Loading time zones…");
        var all = new List<Choice>();
        var shown = new List<Choice>();
        void Filter()
        {
            var q = search.Text.Trim();
            shown = string.IsNullOrEmpty(q) ? all : [.. all.Where(x => Matches(x, q))];
            list.ItemsSource = shown.Select(x => x.Label).ToList();
            status.Text = shown.Count == 0 ? "No matching time zones." : string.Empty;
        }
        list.ContainerContentChanging += (_, args) =>
        {
            if (args.Item is string label && shown.FirstOrDefault(x => string.Equals(x.Label, label, StringComparison.Ordinal)) is { } choice)
            {
                AutomationProperties.SetAutomationId(args.ItemContainer, "timezone.option." + choice.Id);
            }
        };
        search.TextChanged += (_, _) => Filter();
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedIndex >= 0 && list.SelectedIndex < shown.Count) dialog.Complete(shown[list.SelectedIndex].Id);
        };
        panel.Children.Add(search);
        panel.Children.Add(status);
        panel.Children.Add(list);
        panel.Children.Add(SetUiUi.Pill("Cancel", () => dialog.Complete(null), "timezone.cancel"));
        SetUiUi.Run(async () =>
        {
            var now = _time.GetUtcNow();
            var local = _time.LocalTimeZone;
            all = await Task.Run(() => Choices(request.Selected, now, local)).ConfigureAwait(true);
            Filter();
        });
        return panel;
    }

    private static bool Matches(Choice choice, string query)
    {
        if (SettingTimeZoneSearchAliases.Matches(query, choice.Id) || choice.Search.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        var offset = query.StartsWith("GMT", StringComparison.OrdinalIgnoreCase) || query.StartsWith("UTC", StringComparison.OrdinalIgnoreCase) ? query[3..] : query;
        if (offset.Length < 2 || offset[0] is not ('+' or '-')) return false;
        var parts = offset[1..].Split(':', 2);
        if (!int.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var hour)) return false;
        var minute = 0;
        if (parts.Length > 1 && !int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out minute)) return false;
        if (hour > 14 || minute > 59) return false;
        var total = ((hour * 60) + minute) * (offset[0] == '-' ? -1 : 1);
        return choice.Offset == total && choice.Rank >= 0;
    }

    private static List<Choice> Choices(string selected, DateTimeOffset now, TimeZoneInfo local)
    {
        var names = TimeZones.All().ToList();
        if (!string.Equals(selected, "auto", StringComparison.OrdinalIgnoreCase) && !names.Contains(selected, StringComparer.Ordinal)) names.Add(selected);
        var result = new List<Choice> { new("auto", "Automatic (" + local.DisplayName + ")", "automatic auto", -1, int.MinValue) };
        foreach (var name in names)
        {
            var supported = SettingDisplayTimeZone.TryResolve(name, out var zone);
            var offset = supported ? (int)zone!.GetUtcOffset(now).TotalMinutes : 0;
            var span = TimeSpan.FromMinutes(Math.Abs(offset));
            var label = supported ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"GMT{(offset < 0 ? '-' : '+')}{(int)span.TotalHours:00}:{span.Minutes:00}") : "unavailable";
            var city = name.Split('/')[^1].Replace('_', ' ');
            var rank = Array.FindIndex(Common, x => string.Equals(x, name, StringComparison.Ordinal));
            result.Add(new Choice(name, city + "  " + label + "  —  " + name, name.Replace('_', ' ') + " " + city + " " + label + " " + zone?.DisplayName + " " + zone?.StandardName + " " + zone?.DaylightName, rank < 0 ? 100 : rank, offset));
        }
        return [.. result.OrderBy(x => x.Rank).ThenBy(x => x.Offset).ThenBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase)];
    }
}
