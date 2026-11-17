using System.Globalization;
using System.Text.RegularExpressions;

namespace Barbican.Automation.Protocol;

/// <summary>
/// <c>every MS N CMD</c> (Rust automation.rs): before the i-th run every token <c>A+Bi</c> of CMD
/// becomes A + B*i, so one line sweeps the pointer down a list one row per step.
/// </summary>
public static partial class EveryExpansion
{
    public static string Expand(string command, int i)
    {
        ArgumentNullException.ThrowIfNull(command);
        return string.Join(' ', command.Split(' ').Select(w =>
        {
            var m = Step().Match(w);
            if (!m.Success)
            {
                return w;
            }

            var a = double.Parse(m.Groups["a"].Value, CultureInfo.InvariantCulture);
            var b = double.Parse(m.Groups["b"].Value, CultureInfo.InvariantCulture);
            return (a + (b * i)).ToString(CultureInfo.InvariantCulture);
        }));
    }

    [GeneratedRegex(@"^(?<a>-?\d+(?:\.\d+)?)\+(?<b>\d+(?:\.\d+)?)i$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Step();
}
