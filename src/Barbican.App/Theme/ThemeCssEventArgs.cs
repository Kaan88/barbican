namespace Barbican.App.Theme;

/// <summary>The CSS custom-property block of the variant that was just applied.</summary>
public sealed class ThemeCssEventArgs(string css) : EventArgs
{
    public string Css { get; } = css;
}
