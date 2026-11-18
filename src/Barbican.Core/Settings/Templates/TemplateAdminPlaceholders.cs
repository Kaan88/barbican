// Mirrors: Bulwark lib/template-placeholders.ts (the {{name}} placeholders a template carries and the fill dialog on insert, G-034; the names the composer fills itself are {{recipient_name}}, {{recipient_email}}, {{sender_name}}, {{date}}). Contract from the M6 plan section 4.8; owner srv-data.

using Barbican.Core.Common;

namespace Barbican.Core.Settings.Templates;

/// <summary>Placeholders of a template.</summary>
public static class TemplateAdminPlaceholders
{
    /// <summary>The placeholder names in a text, each once, in order of first use ("Hi {{name}}, {{name}} / {{topic}}" gives name, topic); a name is letters, digits, "_" and "-", spaces inside the braces ignored.</summary>
    public static IReadOnlyList<string> Find(string text) => throw new PortPendingException("TemplateAdminPlaceholders.Find");

    /// <summary>The text with each placeholder replaced by its value; a placeholder without a value stays as written.</summary>
    public static string Fill(string text, IReadOnlyDictionary<string, string> values) => throw new PortPendingException("TemplateAdminPlaceholders.Fill");
}
