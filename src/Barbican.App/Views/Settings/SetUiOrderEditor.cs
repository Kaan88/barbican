// Mirrors: Bulwark components/settings/message-list-order-settings.tsx and lib/message-list-order.ts.
// In-window buttons edit up to three server-side sort levels; no popup or real-input automation.
using System.ComponentModel;
using Barbican.Core.Mail.Lists;
using Barbican.Core.Mail.Tags;
using Barbican.Presentation.Settings.Ui;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Settings;

/// <summary>Presets and ordered sort levels for the message list.</summary>
internal sealed class SetUiOrderEditor : StackPanel, IDisposable
{
    private static readonly string[] Criteria = ["unread", "starred", "tag", "receivedAt", "sentAt", "from", "subject", "size"];
    private static readonly string[] Labels = ["Unread", "Starred", "Tag", "Received", "Sent", "Sender", "Subject", "Size"];
    private readonly ISetUiViewModel _vm;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<MessageSortLevel> _levels;
    private int _pending;
    private bool _disposed;

    public SetUiOrderEditor(ISetUiViewModel vm)
    {
        _vm = vm;
        _levels = [.. MessageListOrder.Parse(vm.ValueOf("messageListOrder"))];
        Spacing = 6;
        _vm.PropertyChanged += OnChanged;
        Render();
    }

    void IDisposable.Dispose()
    {
        _vm.PropertyChanged -= OnChanged;
        _disposed = true;
        if (_pending == 0) { _gate.Dispose(); }
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_disposed && string.Equals(e.PropertyName, "Values", StringComparison.Ordinal) && _pending == 0)
        {
            _levels = [.. MessageListOrder.Parse(_vm.ValueOf("messageListOrder"))];
            Render();
        }
    }

    private void Render()
    {
        Children.Clear();
        Children.Add(SetUiUi.Meta("Order is applied by the server to the whole folder. Keyword order requires server support."));
        var tags = TagRules.Parse(_vm.ValueOf("emailKeywords"));
        var presets = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        presets.Children.Add(SetUiUi.Pill("Newest", () => Preset("chronological", null), "set.order.preset.chronological"));
        presets.Children.Add(SetUiUi.Pill("Unread first", () => Preset("unread_first", null), "set.order.preset.unread"));
        presets.Children.Add(SetUiUi.Pill("Starred first", () => Preset("starred_first", null), "set.order.preset.starred"));
        if (tags.Count > 0)
        {
            presets.Children.Add(SetUiUi.Pill("Tagged first", () => Preset("tagged_first", tags[0].Id), "set.order.preset.tagged"));
        }

        Children.Add(presets);
        for (var i = 0; i < _levels.Count; i++)
        {
            Children.Add(Level(i, tags));
        }

        if (_levels.Count < 3)
        {
            Children.Add(SetUiUi.Pill("Add sort level", Add, "set.order.add"));
        }
    }

    private StackPanel Level(int index, IReadOnlyList<TagDef> tags)
    {
        var level = _levels[index];
        var row = new StackPanel { Spacing = 4, Margin = new Thickness(0, 4, 0, 4) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var choices = Criteria.Select((value, i) => (Value: value, Label: Labels[i]))
            .Where(c => string.Equals(c.Value, level.Criterion, StringComparison.Ordinal)
                || ((!string.Equals(c.Value, "tag", StringComparison.Ordinal) || tags.Count > 0)
                    && !_levels.Where((_, i) => i != index).Any(l => string.Equals(l.Criterion, c.Value, StringComparison.Ordinal))))
            .ToList();
        var selected = choices.FindIndex(c => string.Equals(c.Value, level.Criterion, StringComparison.Ordinal));
        buttons.Children.Add(Cycle("set.order.criterion." + index, [.. choices.Select(c => c.Label)], Math.Max(0, selected), at => SetCriterion(index, choices[at].Value, tags)));
        buttons.Children.Add(Cycle("set.order.direction." + index, ["Descending", "Ascending"], string.Equals(level.Direction, "asc", StringComparison.Ordinal) ? 1 : 0, at => Change(index, level with { Direction = at == 1 ? "asc" : "desc" })));
        var up = SetUiUi.Pill("Up", () => Move(index, -1), "set.order.up." + index); up.IsEnabled = index > 0;
        var down = SetUiUi.Pill("Down", () => Move(index, 1), "set.order.down." + index); down.IsEnabled = index + 1 < _levels.Count;
        buttons.Children.Add(up);
        buttons.Children.Add(down);
        buttons.Children.Add(SetUiUi.Pill("Remove", () => Remove(index), "set.order.remove." + index));
        row.Children.Add(buttons);
        if (string.Equals(level.Criterion, "tag", StringComparison.Ordinal) && tags.Count > 0)
        {
            var current = tags.ToList().FindIndex(t => string.Equals(t.Id, level.TagId, StringComparison.Ordinal));
            row.Children.Add(Cycle("set.order.tag." + index, [.. tags.Select(t => t.Label)], Math.Max(0, current), at => Change(index, level with { TagId = tags[at].Id })));
        }

        return row;
    }

    private static Button Cycle(string id, IReadOnlyList<string> labels, int current, Action<int> change)
    {
        var at = Math.Clamp(current, 0, labels.Count - 1);
        var button = SetUiUi.Pill(labels[at] + "  >", () => change((at + 1) % labels.Count), id);
        AutomationProperties.SetName(button, labels[at]);
        return button;
    }

    private void Preset(string preset, string? tag) { _levels = [.. MessageListOrder.Preset(preset, tag)]; Changed(); }
    private void Change(int index, MessageSortLevel level) { _levels[index] = level; Changed(); }
    private void Remove(int index) { _levels.RemoveAt(index); Changed(); }
    private void Move(int index, int direction) { var other = index + direction; (_levels[index], _levels[other]) = (_levels[other], _levels[index]); Changed(); }

    private void SetCriterion(int index, string criterion, IReadOnlyList<TagDef> tags)
    {
        if (_levels.Where((_, i) => i != index).Any(l => string.Equals(l.Criterion, criterion, StringComparison.Ordinal))) { return; }
        if (string.Equals(criterion, "tag", StringComparison.Ordinal) && tags.Count == 0) { return; }
        Change(index, new MessageSortLevel(criterion, criterion is "from" or "subject" ? "asc" : "desc", string.Equals(criterion, "tag", StringComparison.Ordinal) ? tags[0].Id : null));
    }

    private void Add()
    {
        var tags = TagRules.Parse(_vm.ValueOf("emailKeywords"));
        var criterion = Criteria.FirstOrDefault(c => !_levels.Any(l => string.Equals(l.Criterion, c, StringComparison.Ordinal)) && (!string.Equals(c, "tag", StringComparison.Ordinal) || tags.Count > 0));
        if (criterion is not null) { _levels.Add(new MessageSortLevel(criterion, criterion is "from" or "subject" ? "asc" : "desc", string.Equals(criterion, "tag", StringComparison.Ordinal) ? tags[0].Id : null)); Changed(); }
    }

    private void Changed()
    {
        var snapshot = MessageListOrder.ToJson(_levels);
        _pending++;
        Render();
        SetUiUi.Run(async () =>
        {
            await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(true);
            try { await _vm.SetKeyAsync("messageListOrder", snapshot, CancellationToken.None).ConfigureAwait(true); }
            finally
            {
                _ = _gate.Release();
                _pending--;
                if (_pending == 0)
                {
                    if (_disposed) { _gate.Dispose(); }
                    else { _levels = [.. MessageListOrder.Parse(_vm.ValueOf("messageListOrder"))]; Render(); }
                }
            }
        });
    }
}
