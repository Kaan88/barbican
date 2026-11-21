// Mirrors Bulwark lib/plugin-sandbox/host-dialog.ts:56-95 (limits; texts are cut, never refused) and the control/bidi cleaning of PluginToastMethods.
using System.Text;

namespace Barbican.Core.Plugins.Api.Dialogs;

/// <summary>Cuts and cleans add-on dialog text before the host draws it.</summary>
public static class PluginDialogText
{
    /// <summary>Bulwark MAX_DIALOG_TITLE.</summary>
    public const int MaxTitle = 1000;

    /// <summary>Bulwark MAX_DIALOG_MESSAGE.</summary>
    public const int MaxMessage = 20_000;

    /// <summary>Bulwark MAX_DIALOG_LABEL: buttons, field names, labels and placeholders.</summary>
    public const int MaxLabel = 500;

    /// <summary>Bulwark MAX_PROMPT_FIELDS.</summary>
    public const int MaxFields = 100;

    /// <summary>Cuts to <paramref name="max"/> UTF-16 units; controls become spaces (line breaks stay when <paramref name="multiline"/>), bidi overrides vanish, lone surrogates become U+FFFD.</summary>
    public static string Clean(string text, int max, bool multiline)
    {
        ArgumentNullException.ThrowIfNull(text);
        var output = new StringBuilder(Math.Min(text.Length, max));
        for (var index = 0; index < text.Length && output.Length < max; index++)
        {
            var c = text[index];
            if (c == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n') index++;
                c = '\n';
            }

            if (c is '\u0085' or '\u2028' or '\u2029') c = '\n';
            if (c == '\n') { _ = output.Append(multiline ? '\n' : ' '); continue; }
            if (c is <= '\u001f' or >= '\u007f' and <= '\u009f') { _ = output.Append(' '); continue; }
            if (c is '\u061c' or '\u200e' or '\u200f' or >= '\u202a' and <= '\u202e' or >= '\u2066' and <= '\u2069') continue;
            if (char.IsHighSurrogate(c))
            {
                if (index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
                {
                    if (output.Length + 2 > max) break;
                    _ = output.Append(c).Append(text[++index]);
                }
                else _ = output.Append('\ufffd');
            }
            else _ = output.Append(char.IsLowSurrogate(c) ? '\ufffd' : c);
        }

        return multiline ? output.ToString().Trim('\n', ' ') : output.ToString().Trim();
    }

    /// <summary>Cuts a key that goes back to the add-on and is never drawn; only the length limit applies.</summary>
    public static string CutKey(string name)
    {
        if (name.Length <= MaxLabel) return name;
        return name[..(char.IsHighSurrogate(name[MaxLabel - 1]) ? MaxLabel - 1 : MaxLabel)];
    }
}
