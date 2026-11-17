// Mirrors: M1-plan §4.3 (Rows: MessageRowViewModel | GroupHeaderViewModel); Bulwark components/email/list-date-group-header.tsx and email-list.tsx:158 (section headers).

using Barbican.Presentation.Mail;

namespace Barbican.App.Views.List;

/// <summary>Picks the date-group header template or the message row template.</summary>
public sealed partial class MessageRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? MessageTemplate { get; set; }

    public DataTemplate? HeaderTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item is GroupHeaderViewModel ? HeaderTemplate : MessageTemplate;

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
