// Not Rust rows: I-434 (Bulwark filter-rule-modal.tsx: the comparator and the value box follow the field). M6 stream srv-ui.

using Barbican.Core.Filters.Rules;
using Barbican.Presentation.Settings.Server;

namespace Barbican.Presentation.Tests.Settings.Server;

public sealed class SrvUiFilterFormRulesTests
{
    [Fact]
    public void Changing_the_field_keeps_a_comparator_it_allows_and_resets_one_it_does_not()
    {
        var kept = SrvUiFilterFormRules.FieldChanged(new FilterCond("from", "contains", "x@y.example", string.Empty), "to");
        Assert.Equal(new FilterCond("to", "contains", "x@y.example", string.Empty), kept);
        var reset = SrvUiFilterFormRules.FieldChanged(new FilterCond("subject", "starts_with", "Re:", string.Empty), "size");
        Assert.Equal("greater_than", reset.Comparator);
        Assert.Equal(string.Empty, reset.Value);
        var header = SrvUiFilterFormRules.FieldChanged(new FilterCond("header", "contains", "v", "X-Spam"), "subject");
        Assert.Equal(string.Empty, header.Header);
    }

    [Fact]
    public void The_value_box_follows_the_field_and_comparator()
    {
        Assert.Equal(SrvUiValueKind.None, SrvUiFilterFormRules.ValueKind("all", string.Empty));
        Assert.Equal(SrvUiValueKind.None, SrvUiFilterFormRules.ValueKind("attachment", "has_any"));
        Assert.Equal(SrvUiValueKind.AttachmentType, SrvUiFilterFormRules.ValueKind("attachment", "has_type"));
        Assert.Equal(SrvUiValueKind.Size, SrvUiFilterFormRules.ValueKind("size", "less_than"));
        Assert.Equal(SrvUiValueKind.Text, SrvUiFilterFormRules.ValueKind("subject", "regex"));
    }
}
