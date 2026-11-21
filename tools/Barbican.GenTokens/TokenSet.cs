using System.Globalization;
using System.Text;

namespace Barbican.GenTokens;

/// <summary>Everything gen-tokens extracts from theme.slint, evaluated.</summary>
/// <param name="ColorKeys">Palette keys (PascalCase) in source order.</param>
/// <param name="Palettes">Per variant (index order of <see cref="Variant.All"/>): key → ARGB.</param>
/// <param name="ShapeKeys">Shape and type-scale keys (PascalCase) in source order.</param>
/// <param name="ShapeBento">Shape values for Bento, in px.</param>
/// <param name="ShapeNordic">Shape values for Nordic, in px.</param>
/// <param name="ShapeBulwark">Shape values for Bulwark, in px.</param>
/// <param name="Motion">Motion tokens at reduce = false, rapid = false: key → (value, unit suffix).</param>
/// <param name="Ease">Easing curves: key → control points.</param>
/// <param name="Icons">Line-icon path data (24 × 24 box): key → SVG path.</param>
public sealed record TokenSet(
    IReadOnlyList<string> ColorKeys,
    IReadOnlyList<IReadOnlyDictionary<string, uint>> Palettes,
    IReadOnlyList<string> ShapeKeys,
    IReadOnlyDictionary<string, double> ShapeBento,
    IReadOnlyDictionary<string, double> ShapeNordic,
    IReadOnlyDictionary<string, double> ShapeBulwark,
    IReadOnlyList<(string Key, double Value, string Unit)> Motion,
    IReadOnlyList<(string Key, EasingValue Curve)> Ease,
    IReadOnlyList<(string Key, string Path)> Icons)
{
    /// <summary>kebab-case → PascalCase ("on-accent" → "OnAccent", "r-tile" → "RTile").</summary>
    public static string Pascal(string kebab)
    {
        ArgumentNullException.ThrowIfNull(kebab);
        var sb = new StringBuilder(kebab.Length);
        foreach (var part in kebab.Split('-', StringSplitOptions.RemoveEmptyEntries))
        {
            sb.Append(char.ToUpperInvariant(part[0])).Append(part.AsSpan(1));
        }

        return sb.ToString();
    }

    /// <summary>Reads theme.slint and evaluates every token for every variant.</summary>
    public static TokenSet FromSlint(string source)
    {
        var globals = SlintTheme.Parse(source).ToDictionary(g => g.Name, StringComparer.Ordinal);
        var theme = Need(globals, "Theme");
        var colorProps = OutputsOfType(theme, "color");
        var lengthProps = OutputsOfType(theme, "length");
        var (palettes, bento, nordic) = EvaluateTheme(theme, colorProps, lengthProps);
        AddBulwarkPalettes(Need(globals, "Bulwark"), colorProps.Select(p => Pascal(p.Name)).ToList(), palettes);
        var bulwarkShape = EvaluateBulwarkShape(theme, lengthProps);

        var ease = Need(globals, "Ease");
        var easeEval = new SlintEvaluator(ease, NoInputs);
        var easeTokens = ease.Properties
            .Select(p => (Pascal(p.Name), easeEval.Get(p.Name) as EasingValue ?? throw new FormatException($"Ease.{p.Name} is not cubic-bezier()")))
            .ToList();

        var icons = Need(globals, "Icons");
        var iconEval = new SlintEvaluator(icons, NoInputs);
        var iconTokens = icons.Properties
            .Select(p => (Pascal(p.Name), iconEval.Get(p.Name) is StringValue s ? s.Value : throw new FormatException($"Icons.{p.Name} is not a string")))
            .ToList();

        return new TokenSet(
            colorProps.Select(p => Pascal(p.Name)).ToList(),
            palettes,
            lengthProps.Select(p => Pascal(p.Name)).ToList(),
            bento,
            nordic,
            bulwarkShape,
            EvaluateMotion(Need(globals, "Motion")),
            easeTokens,
            iconTokens);
    }

    private static readonly Dictionary<string, SlintValue> NoInputs = new(StringComparer.Ordinal);

    private static List<SlintProperty> OutputsOfType(SlintGlobal global, string type) =>
        global.Properties.Where(p => string.Equals(p.Type, type, StringComparison.Ordinal) && !p.IsInput).ToList();

    private static (List<IReadOnlyDictionary<string, uint>> Palettes, Dictionary<string, double> Bento, Dictionary<string, double> Nordic) EvaluateTheme(
        SlintGlobal theme, List<SlintProperty> colorProps, List<SlintProperty> lengthProps)
    {
        var palettes = new List<IReadOnlyDictionary<string, uint>>();
        var shapes = new Dictionary<bool, Dictionary<string, double>>();
        foreach (var v in Variant.All)
        {
            var eval = new SlintEvaluator(theme, ThemeInputs(v));
            palettes.Add(colorProps.ToDictionary(
                p => Pascal(p.Name),
                p => eval.Get(p.Name) is ColorValue c ? c.Argb : throw new FormatException($"Theme.{p.Name} is not a colour for {v}"),
                StringComparer.Ordinal));

            var shape = lengthProps.ToDictionary(
                p => Pascal(p.Name),
                p => eval.Get(p.Name) is LengthValue l ? l.Px : throw new FormatException($"Theme.{p.Name} is not a length for {v}"),
                StringComparer.Ordinal);

            // Shape tokens depend on the style only: every variant of one style must agree.
            if (!shapes.TryAdd(v.Nordic, shape))
            {
                var differs = shape.FirstOrDefault(kv => !shapes[v.Nordic][kv.Key].Equals(kv.Value));
                if (differs.Key is not null)
                {
                    throw new FormatException($"Theme.{differs.Key} depends on more than the style ({v}); gen-tokens cannot express that yet");
                }
            }
        }

        return (palettes, shapes[false], shapes[true]);
    }

    /// <summary>The shape tokens of the Bulwark style (style = 2); its palette is the Bulwark global, so the other inputs are irrelevant.</summary>
    private static Dictionary<string, double> EvaluateBulwarkShape(SlintGlobal theme, List<SlintProperty> lengthProps)
    {
        var inputs = ThemeInputs(Variant.All[0]).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        inputs["style"] = new NumberValue(2);
        var eval = new SlintEvaluator(theme, inputs);
        return lengthProps.ToDictionary(
            p => Pascal(p.Name),
            p => eval.Get(p.Name) is LengthValue l ? l.Px : throw new FormatException($"Theme.{p.Name} is not a length for Bulwark"),
            StringComparer.Ordinal);
    }

    private static void AddBulwarkPalettes(SlintGlobal bulwark, IReadOnlyList<string> colorKeys, List<IReadOnlyDictionary<string, uint>> palettes)
    {
        // The Bulwark palette is authored in theme.slint (`export global Bulwark`); it must define exactly the colours of `Theme`.
        var own = OutputsOfType(bulwark, "color").Select(p => Pascal(p.Name)).ToList();
        var missing = colorKeys.Except(own, StringComparer.Ordinal).ToList();
        var extra = own.Except(colorKeys, StringComparer.Ordinal).ToList();
        if (missing.Count > 0 || extra.Count > 0)
        {
            throw new FormatException($"Bulwark and Theme define different colours: missing [{string.Join(", ", missing)}], extra [{string.Join(", ", extra)}]");
        }

        // Variant order after the eight legacy ones: dark, dark more-contrast, light, light more-contrast (ThemeVariant.Index).
        foreach (var (dark, more) in new[] { (true, false), (true, true), (false, false), (false, true) })
        {
            var eval = new SlintEvaluator(bulwark, new Dictionary<string, SlintValue>(StringComparer.Ordinal)
            {
                ["dark"] = new BoolValue(dark),
                ["more-contrast"] = new BoolValue(more),
            });
            palettes.Add(OutputsOfType(bulwark, "color").ToDictionary(
                p => Pascal(p.Name),
                p => eval.Get(p.Name) is ColorValue c ? c.Argb : throw new FormatException($"Bulwark.{p.Name} is not a colour"),
                StringComparer.Ordinal));
        }
    }
    private static List<(string Key, double Value, string Unit)> EvaluateMotion(SlintGlobal motion)
    {
        var eval = new SlintEvaluator(motion, new Dictionary<string, SlintValue>(StringComparer.Ordinal)
        {
            ["reduce"] = new BoolValue(false),
            ["rapid"] = new BoolValue(false),
        });
        return motion.Properties
            .Where(p => !p.IsInput && !string.Equals(p.Name, "k", StringComparison.Ordinal))
            .Select(p => eval.Get(p.Name) switch
            {
                DurationValue d => (Pascal(p.Name), d.Ms, "Ms"),
                LengthValue l => (Pascal(p.Name), l.Px, "Px"),
                NumberValue n => (Pascal(p.Name), n.Value, ""),
                var other => throw new FormatException($"Motion.{p.Name}: unsupported value {other}"),
            })
            .ToList();
    }

    /// <summary>The inputs of the Theme global for one variant.</summary>
    public static IReadOnlyDictionary<string, SlintValue> ThemeInputs(Variant v) => new Dictionary<string, SlintValue>(StringComparer.Ordinal)
    {
        ["dark"] = new BoolValue(v.Dark),
        ["more-contrast"] = new BoolValue(v.MoreContrast),
        ["style"] = new NumberValue(v.Nordic ? 1 : 0),
    };

    private static SlintGlobal Need(Dictionary<string, SlintGlobal> globals, string name) =>
        globals.TryGetValue(name, out var g) ? g : throw new FormatException($"theme.slint has no 'export global {name}'");

    internal static string Num(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
