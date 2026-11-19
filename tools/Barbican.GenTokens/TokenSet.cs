using System.Globalization;
using System.Text;

namespace Barbican.GenTokens;

/// <summary>Everything gen-tokens extracts from theme.slint, evaluated.</summary>
/// <param name="ColorKeys">Palette keys (PascalCase) in source order.</param>
/// <param name="Palettes">Per variant (index order of <see cref="Variant.All"/>): key → ARGB.</param>
/// <param name="ShapeKeys">Shape and type-scale keys (PascalCase) in source order.</param>
/// <param name="ShapeBento">Shape values for Bento, in px.</param>
/// <param name="ShapeNordic">Shape values for Nordic, in px.</param>
/// <param name="Motion">Motion tokens at reduce = false, rapid = false: key → (value, unit suffix).</param>
/// <param name="Ease">Easing curves: key → control points.</param>
/// <param name="Icons">Line-icon path data (24 × 24 box): key → SVG path.</param>
public sealed record TokenSet(
    IReadOnlyList<string> ColorKeys,
    IReadOnlyList<IReadOnlyDictionary<string, uint>> Palettes,
    IReadOnlyList<string> ShapeKeys,
    IReadOnlyDictionary<string, double> ShapeBento,
    IReadOnlyDictionary<string, double> ShapeNordic,
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

        AddBulwarkPalettes(palettes);
        return (palettes, shapes[false], shapes[true]);
    }

    private static void AddBulwarkPalettes(List<IReadOnlyDictionary<string, uint>> palettes)
    {
        // Bulwark 1.21.2 app/globals.css:46-93,127-170 and
        // app/accessibility.css:290-329. Semantic mappings onto the existing native
        // brush names; legacy sender tint pairs remain unchanged until redesigned.
        foreach (var (dark, more) in new[] { (true, false), (true, true), (false, false), (false, true) })
        {
            var p = new Dictionary<string, uint>(palettes[(dark ? 0 : 4) + (more ? 2 : 0)], StringComparer.Ordinal);
            void Set(string key, uint d, uint l) => p[key] = dark ? d : l;
            Set("Ground", 0xFF0A0A0A, 0xFFFFFFFF);
            Set("Tile", 0xFF141414, 0xFFFFFFFF);
            Set("Tile2", 0xFF262626, 0xFFF8FAFC);
            Set("Line", more ? 0xFF9CA3AF : 0x4D808080, more ? 0xFF4B5563 : 0xFFE2E8F0);
            Set("Track", 0xFF262626, 0xFFF1F5F9);
            Set("Text", more ? 0xFFFFFFFF : 0xFFFAFAFA, more ? 0xFF000000 : 0xFF0F172A);
            p["Body"] = p["Text"];
            Set("Dim", more ? 0xFFE5E7EB : 0xFFADADAD, more ? 0xFF1F2937 : 0xFF59687C);
            p["Faint"] = p["Dim"];
            Set("Accent", more ? 0xFF93C5FD : 0xFFFAFAFA, more ? 0xFF1D4ED8 : 0xFF185AEA);
            Set("OnAccent", more ? 0xFF000000 : 0xFF171717, 0xFFFFFFFF);
            Set("Sel", 0x403B82F6, 0xFFDBEAFE);
            Set("SelText", more ? 0xFFDBEAFE : 0xFF93C5FD, more ? 0xFF1E3A8A : 0xFF1E40AF);
            Set("Popup", 0xFF1C1C1C, 0xFFFFFFFF);
            p["Bg"] = p["Ground"];
            Set("Rail", 0xFF0A0A0A, 0xFFF8FAFC);
            p["Side"] = p["Rail"];
            Set("Hover", 0xFF1A1A1A, 0xFFF1F5F9);
            p["Pressed"] = p["Tile2"];
            p["Chip"] = p["Tile2"];
            p["Raised"] = p["Tile2"];
            p["Zebra"] = p["Ground"];
            p["Bubble"] = p["Tile2"];
            p["Reader"] = p["Ground"];
            p["MutedDay"] = p["Dim"];
            Set("Ring", more ? 0xFFFFFFFF : 0xFFD4D4D4, more ? 0xFF000000 : 0xFF2563EB);
            palettes.Add(p);
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
