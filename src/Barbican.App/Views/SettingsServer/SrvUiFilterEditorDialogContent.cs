// Mirrors: Bulwark components/filters/filter-rule-modal.tsx (the rule form: name, match all or any, condition rows, action rows, "Don't apply later rules", spam opt-in, active period), Rust pim-filters.slint; I-432..I-434. Draws SrvUiFilterEditorRequest; the model owns the rules of the form. Dialog shell and footer: ui/dialog.tsx:131-134,181 through Views/Dialogs/DialogStyles.
// No drop-down: a field, comparator or folder is a stepping button (ADR 0007); the period is typed as days. Owner srv-ui.

using Barbican.App.Views.Dialogs;
using Barbican.Core.Filters.Rules;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Settings.Server;
using Microsoft.UI.Xaml.Automation;
using static Barbican.App.Views.Calendar.Editors.CalendarCardKit;

namespace Barbican.App.Views.SettingsServer;

/// <summary>Draws <see cref="SrvUiFilterEditorRequest"/>.</summary>
public sealed class SrvUiFilterEditorDialogContent : IDialogContent
{
    public bool OwnButtons => true;

    public double MaxWidth => 640;

    public bool CanDraw(object request) => request is SrvUiFilterEditorRequest;

    public UIElement Create(ActiveDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var m = ((SrvUiFilterEditorRequest)dialog.Request).Model;
        var host = new StackPanel { Spacing = 6, MinWidth = 560 };
        AutomationProperties.SetAutomationId(host, "srv.rule-editor");
        AutomationProperties.SetName(host, m.Heading);
        m.Finished += (_, _) => dialog.Complete(m.Saved is not null);
        m.Changed += (_, _) => host.DispatcherQueue.TryEnqueue(() => Fill(host, m));
        Fill(host, m);
        return new ScrollViewer { Content = host, MaxHeight = 620, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static void Fill(StackPanel host, SrvUiFilterEditorModel m)
    {
        host.Children.Clear();
        host.Children.Add(Label("Name"));
        host.Children.Add(Text("srv.rule.name", m.Name, v => m.Name = v, placeholder: "Newsletters"));
        host.Children.Add(Label("When"));
        host.Children.Add(SrvUiKit.Cycle("srv.rule.match", ["All of these match", "Any of these match"], m.Any ? 1 : 0, i => m.Any = i == 1));
        for (var i = 0; i < m.Conditions.Count; i++)
        {
            host.Children.Add(ConditionRow(m, i));
        }

        host.Children.Add(SrvUiKit.Small("Add condition", "srv.rule.add-condition", m.AddCondition));
        host.Children.Add(Label("Then"));
        for (var i = 0; i < m.Acts.Count; i++)
        {
            host.Children.Add(ActionRow(m, i));
        }

        host.Children.Add(SrvUiKit.Small("Add action", "srv.rule.add-action", m.AddAction));
        host.Children.Add(Check("srv.rule.stop", "Don't apply later rules", m.Stop, v => m.Stop = v));
        if (m.HasFolderAction)
        {
            host.Children.Add(Check("srv.rule.spam", "Also move or copy messages the server marked as spam", m.IncludeSpam, v => m.IncludeSpam = v));
        }

        if (m.PeriodsSupported)
        {
            host.Children.Add(Label("Only between (days, yyyy-MM-dd; either can stay empty)"));
            host.Children.Add(SrvUiKit.Row(Text("srv.rule.from", m.From, v => m.From = v, placeholder: "From"), Text("srv.rule.until", m.Until, v => m.Until = v, placeholder: "Until")));
        }
        else
        {
            host.Children.Add(Body("This server cannot limit a rule to a period."));
        }

        host.Children.Add(SrvUiKit.Error(m.Error));
        host.Children.Add(Barbican.App.Views.Dialogs.DialogStyles.Footer(
            SrvUiKit.Foot("Cancel", DialogTone.Plain, "srv.rule.cancel", m.Cancel),
            SrvUiKit.Foot("Save rule", DialogTone.Primary, "srv.rule.save", () => SrvUiKit.Run(() => m.SaveAsync(CancellationToken.None)), !m.Saving)));
    }

    private static StackPanel ConditionRow(SrvUiFilterEditorModel m, int index)
    {
        var c = m.Conditions[index];
        var fields = FilterRules.Fields;
        var row = new StackPanel { Spacing = 4 };
        var line = SrvUiKit.Row();
        var fieldAt = Math.Max(0, fields.Select(f => f.Id).ToList().IndexOf(c.Field));
        line.Children.Add(SrvUiKit.Cycle("srv.rule.field." + index, [.. fields.Select(f => f.Label)], fieldAt, i => m.SetField(index, fields[i].Id)));
        var comparators = m.ComparatorsFor(index);
        if (comparators.Count > 0)
        {
            line.Children.Add(SrvUiKit.Cycle("srv.rule.cmp." + index, [.. comparators.Select(Humanize)], Math.Max(0, comparators.ToList().IndexOf(c.Comparator)), i => m.SetComparator(index, comparators[i])));
        }

        line.Children.Add(SrvUiKit.Small("Remove", "srv.rule.remove-condition." + index, () => m.RemoveCondition(index), true));
        row.Children.Add(line);
        if (string.Equals(c.Field, "header", StringComparison.Ordinal))
        {
            row.Children.Add(Text("srv.rule.header." + index, c.Header, v => m.SetHeader(index, v), placeholder: "Header name (X-Spam-Status)"));
        }

        var kind = SrvUiFilterFormRules.ValueKind(c.Field, c.Comparator);
        if (kind != SrvUiValueKind.None)
        {
            var hint = kind switch
            {
                SrvUiValueKind.Size => "Size, such as 500K or 10M",
                SrvUiValueKind.AttachmentType => "Attachment type, such as application/pdf or image/",
                _ => "Text (separate several with commas)",
            };
            row.Children.Add(Text("srv.rule.value." + index, c.Value, v => m.SetValue(index, v), placeholder: hint));
        }

        return row;
    }

    private static StackPanel ActionRow(SrvUiFilterEditorModel m, int index)
    {
        var a = m.Acts[index];
        var row = new StackPanel { Spacing = 4 };
        var line = SrvUiKit.Row();
        var kinds = m.Actions;
        line.Children.Add(SrvUiKit.Cycle("srv.rule.action." + index, [.. kinds.Select(k => k.Label)], Math.Max(0, kinds.Select(k => k.Id).ToList().IndexOf(a.Kind)), i => m.SetActionKind(index, kinds[i].Id)));
        line.Children.Add(SrvUiKit.Small("Remove", "srv.rule.remove-action." + index, () => m.RemoveAction(index), true));
        row.Children.Add(line);
        switch (a.Kind)
        {
            case "move" or "copy":
                var folders = m.Folders;
                if (folders.Count == 0)
                {
                    row.Children.Add(Body("No folders to choose from yet."));
                }
                else
                {
                    var at = folders.ToList().FindIndex(f => string.Equals(f.Id, a.Mailbox, StringComparison.Ordinal));
                    if (at < 0)
                    {
                        m.SetActionFolder(index, folders[0].Id);
                        at = 0;
                    }

                    row.Children.Add(SrvUiKit.Cycle("srv.rule.folder." + index, [.. folders.Select(f => f.Path)], at, i => m.SetActionFolder(index, folders[i].Id)));
                }

                break;
            case "forward":
                row.Children.Add(Text("srv.rule.forward." + index, a.Value, v => m.SetActionValue(index, v), placeholder: "Forward to (address)"));
                row.Children.Add(Check("srv.rule.keep-copy." + index, "Keep a copy in the inbox", a.KeepCopy, v => m.SetKeepCopy(index, v)));
                break;
            case "add_label":
                row.Children.Add(Text("srv.rule.tag." + index, a.Value, v => m.SetActionValue(index, v), placeholder: "Tag"));
                break;
            case "reject":
                row.Children.Add(Text("srv.rule.reject." + index, a.Value, v => m.SetActionValue(index, v), multiline: true, placeholder: "Message sent back to the sender"));
                break;
            default:
                break;
        }

        return row;
    }

    private static string Humanize(string comparator) => comparator switch
    {
        "greater_than" => "is larger than",
        "less_than" => "is smaller than",
        "has_any" => "has any attachment",
        "has_type" => "has attachment of type",
        "address_is" => "address is",
        "domain_is" => "domain is",
        "regex" => "matches regex",
        "matches" => "matches pattern",
        _ => comparator.Replace('_', ' '),
    };
}
