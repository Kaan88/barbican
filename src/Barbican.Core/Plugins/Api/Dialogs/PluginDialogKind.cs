namespace Barbican.Core.Plugins.Api.Dialogs;

/// <summary>The three fixed-form dialogs an add-on can ask the host to draw.</summary>
public enum PluginDialogKind
{
    /// <summary>One button; the call resolves to undefined after dismissal.</summary>
    Alert,

    /// <summary>OK and Cancel; the call resolves to true or false.</summary>
    Confirm,

    /// <summary>Text fields; the call resolves to a name to value map, or null on cancel.</summary>
    Prompt,
}
