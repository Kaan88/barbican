// Mirrors: M3 plan 6.3 step 2 (an AutomationPeer with a text provider for Narrator). The document text is exposed as the control's name and value; a full TextPattern is not built (recorded in the report).

using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;

namespace Barbican.App.Views.Editor;

/// <summary>The peer of the editor: an edit control whose value is the document text.</summary>
public sealed class EditorAutomationPeer(EditorView owner) : FrameworkElementAutomationPeer(owner), IValueProvider
{
    public bool IsReadOnly => false;

    public string Value => owner.PlainText();

    public void SetValue(string value) => owner.TypeText(value);

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Edit;

    protected override string GetClassNameCore() => nameof(EditorView);

    protected override object? GetPatternCore(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Value ? this : base.GetPatternCore(patternInterface);
}
