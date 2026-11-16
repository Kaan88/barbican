using System.Globalization;

namespace Barbican.GenTokens;

/// <summary>
/// Evaluates the property expressions of one Slint global for a fixed set of inputs. Supports what
/// theme.slint uses: literals (colour, length, duration, number, string, bool), references to other
/// properties of the same global, ternaries, ==, !=, !, &amp;&amp;, ||, + - * /, parentheses and
/// cubic-bezier(). Anything else is an error, so a new construct in theme.slint fails loudly.
/// </summary>
public sealed class SlintEvaluator
{
    private readonly SlintGlobal _global;
    private readonly IReadOnlyDictionary<string, SlintValue> _inputs;
    private readonly Dictionary<string, SlintValue> _cache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _evaluating = new(StringComparer.Ordinal);

    public SlintEvaluator(SlintGlobal global, IReadOnlyDictionary<string, SlintValue> inputs)
    {
        _global = global ?? throw new ArgumentNullException(nameof(global));
        _inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
    }

    /// <summary>The value of a property of this global.</summary>
    public SlintValue Get(string name)
    {
        if (_inputs.TryGetValue(name, out var input))
        {
            return input;
        }

        if (_cache.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var prop = _global.Find(name) ?? throw new FormatException($"{_global.Name}: unknown property '{name}'");
        if (prop.Expression is null)
        {
            throw new FormatException($"{_global.Name}.{name} (line {prop.Line}) has no value and no input");
        }

        if (!_evaluating.Add(name))
        {
            throw new FormatException($"{_global.Name}.{name}: cyclic reference");
        }

        try
        {
            var parser = new Parser(Tokenize(prop.Expression, prop), this, prop);
            var value = parser.ParseAll();
            _cache[name] = value;
            return value;
        }
        finally
        {
            _evaluating.Remove(name);
        }
    }

    private enum Kind
    {
        Color,
        Number,
        String,
        Ident,
        Op,
    }

    private readonly record struct Token(Kind Kind, string Text, string Unit = "");

    private static List<Token> Tokenize(string text, SlintProperty prop)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            var start = i;
            Token token;
            if (c == '#')
            {
                i = Scan(text, i + 1, Uri.IsHexDigit);
                token = new Token(Kind.Color, text[start..i]);
            }
            else if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1])))
            {
                var numberEnd = Scan(text, i, ch => char.IsAsciiDigit(ch) || ch == '.');
                i = Scan(text, numberEnd, ch => char.IsAsciiLetter(ch) || ch == '%');
                token = new Token(Kind.Number, text[start..numberEnd], text[numberEnd..i]);
            }
            else if (c == '"')
            {
                var close = text.IndexOf('"', i + 1);
                i = close >= 0 ? close + 1 : throw new FormatException($"Line {prop.Line}: unterminated string");
                token = new Token(Kind.String, text[(start + 1)..close]);
            }
            else if (char.IsAsciiLetter(c) || c == '_')
            {
                i = Scan(text, i, ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-');
                token = new Token(Kind.Ident, text[start..i]);
            }
            else
            {
                token = ReadOperator(text, i, prop);
                i += token.Text.Length;
            }

            tokens.Add(token);
        }

        return tokens;
    }

    private static int Scan(string text, int from, Func<char, bool> accept)
    {
        var i = from;
        while (i < text.Length && accept(text[i]))
        {
            i++;
        }

        return i;
    }

    private static Token ReadOperator(string text, int i, SlintProperty prop)
    {
        var two = i + 1 < text.Length ? text.Substring(i, 2) : string.Empty;
        if (two is "==" or "!=" or "&&" or "||")
        {
            return new Token(Kind.Op, two);
        }

        return "?:()+-*/!,".Contains(text[i], StringComparison.Ordinal)
            ? new Token(Kind.Op, text[i].ToString())
            : throw new FormatException($"Line {prop.Line}: unexpected '{text[i]}' in '{text}'");
    }

    private sealed class Parser(List<Token> tokens, SlintEvaluator owner, SlintProperty prop)
    {
        private int _pos;

        public SlintValue ParseAll()
        {
            var value = Ternary();
            if (_pos != tokens.Count)
            {
                throw Error($"unexpected '{tokens[_pos].Text}'");
            }

            return value;
        }

        private FormatException Error(string message) =>
            new($"{owner._global.Name}.{prop.Name} (line {prop.Line}): {message} in '{prop.Expression}'");

        private bool Accept(string op)
        {
            if (_pos < tokens.Count && tokens[_pos].Kind == Kind.Op && string.Equals(tokens[_pos].Text, op, StringComparison.Ordinal))
            {
                _pos++;
                return true;
            }

            return false;
        }

        private void Expect(string op)
        {
            if (!Accept(op))
            {
                throw Error($"expected '{op}'");
            }
        }

        private SlintValue Ternary()
        {
            var cond = Or();
            if (!Accept("?"))
            {
                return cond;
            }

            // Both branches are parsed (so the token position is right), only one is kept.
            var whenTrue = Ternary();
            Expect(":");
            var whenFalse = Ternary();
            return AsBool(cond) ? whenTrue : whenFalse;
        }

        private SlintValue Or()
        {
            var left = And();
            while (Accept("||"))
            {
                var right = And();
                left = new BoolValue(AsBool(left) || AsBool(right));
            }

            return left;
        }

        private SlintValue And()
        {
            var left = Equality();
            while (Accept("&&"))
            {
                var right = Equality();
                left = new BoolValue(AsBool(left) && AsBool(right));
            }

            return left;
        }

        private SlintValue Equality()
        {
            var left = Additive();
            if (Accept("=="))
            {
                return new BoolValue(Equals(left, Additive()));
            }

            if (Accept("!="))
            {
                return new BoolValue(!Equals(left, Additive()));
            }

            return left;
        }

        private SlintValue Additive()
        {
            var left = Multiplicative();
            while (true)
            {
                if (Accept("+"))
                {
                    left = Add(left, Multiplicative(), 1);
                }
                else if (Accept("-"))
                {
                    left = Add(left, Multiplicative(), -1);
                }
                else
                {
                    return left;
                }
            }
        }

        private SlintValue Multiplicative()
        {
            var left = Unary();
            while (true)
            {
                if (Accept("*"))
                {
                    left = Multiply(left, Unary());
                }
                else if (Accept("/"))
                {
                    left = Divide(left, Unary());
                }
                else
                {
                    return left;
                }
            }
        }

        private SlintValue Unary()
        {
            if (Accept("!"))
            {
                return new BoolValue(!AsBool(Unary()));
            }

            if (Accept("-"))
            {
                return Multiply(new NumberValue(-1), Unary());
            }

            return Primary();
        }

        private SlintValue Primary()
        {
            if (_pos >= tokens.Count)
            {
                throw Error("unexpected end");
            }

            var t = tokens[_pos++];
            switch (t.Kind)
            {
                case Kind.Color:
                    return ColorValue.ParseSlint(t.Text);
                case Kind.String:
                    return new StringValue(t.Text);
                case Kind.Number:
                    var n = double.Parse(t.Text, NumberStyles.Float, CultureInfo.InvariantCulture);
                    return t.Unit switch
                    {
                        "" => new NumberValue(n),
                        "px" => new LengthValue(n),
                        "ms" => new DurationValue(n),
                        "s" => new DurationValue(n * 1000),
                        _ => throw Error($"unsupported unit '{t.Unit}'"),
                    };
                case Kind.Ident when t.Text is "true" or "false":
                    return new BoolValue(string.Equals(t.Text, "true", StringComparison.Ordinal));
                case Kind.Ident when string.Equals(t.Text, "cubic-bezier", StringComparison.Ordinal):
                    Expect("(");
                    var args = new double[4];
                    for (var i = 0; i < 4; i++)
                    {
                        if (i > 0)
                        {
                            Expect(",");
                        }

                        args[i] = AsNumber(Ternary());
                    }

                    Expect(")");
                    return new EasingValue(args[0], args[1], args[2], args[3]);
                case Kind.Ident:
                    if (_pos < tokens.Count && tokens[_pos] is { Kind: Kind.Op, Text: "(" })
                    {
                        throw Error($"unsupported function '{t.Text}'");
                    }

                    return owner.Get(t.Text);
                case Kind.Op when string.Equals(t.Text, "(", StringComparison.Ordinal):
                    var inner = Ternary();
                    Expect(")");
                    return inner;
                default:
                    throw Error($"unexpected '{t.Text}'");
            }
        }

        private bool AsBool(SlintValue v) => v is BoolValue b ? b.Value : throw Error($"expected a bool, got {v}");

        private double AsNumber(SlintValue v) => v is NumberValue n ? n.Value : throw Error($"expected a number, got {v}");

        private SlintValue Add(SlintValue a, SlintValue b, int sign) => (a, b) switch
        {
            (LengthValue x, LengthValue y) => new LengthValue(x.Px + (sign * y.Px)),
            (DurationValue x, DurationValue y) => new DurationValue(x.Ms + (sign * y.Ms)),
            (NumberValue x, NumberValue y) => new NumberValue(x.Value + (sign * y.Value)),
            _ => throw Error($"cannot add {a} and {b}"),
        };

        private SlintValue Multiply(SlintValue a, SlintValue b) => (a, b) switch
        {
            (NumberValue x, NumberValue y) => new NumberValue(x.Value * y.Value),
            (LengthValue x, NumberValue y) => new LengthValue(x.Px * y.Value),
            (NumberValue x, LengthValue y) => new LengthValue(x.Value * y.Px),
            (DurationValue x, NumberValue y) => new DurationValue(x.Ms * y.Value),
            (NumberValue x, DurationValue y) => new DurationValue(x.Value * y.Ms),
            _ => throw Error($"cannot multiply {a} and {b}"),
        };

        private SlintValue Divide(SlintValue a, SlintValue b) => (a, b) switch
        {
            (NumberValue x, NumberValue y) => new NumberValue(x.Value / y.Value),
            (LengthValue x, NumberValue y) => new LengthValue(x.Px / y.Value),
            (LengthValue x, LengthValue y) => new NumberValue(x.Px / y.Px),
            (DurationValue x, NumberValue y) => new DurationValue(x.Ms / y.Value),
            _ => throw Error($"cannot divide {a} by {b}"),
        };
    }
}
