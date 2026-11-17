using System.Text.RegularExpressions;
using Barbican.Core.Mail.Body;

namespace Barbican.Core.Tests.Mail.Body;

/// <summary>
/// The body shell never navigates and never lets a message navigate it (docs/adr/0005 decision 4 and
/// "M0 review"; on Linux a script-initiated top-level navigation of the shell reached the network
/// although NavigationStarting cancelled it, so the only safe shell is one that never starts one).
/// Static checks of src/Barbican.App/Assets/mail/shell.js with its comments removed.
/// </summary>
public sealed partial class ShellScriptTests
{
    private static readonly Lazy<string> Code = new(() => StripComments(File.ReadAllText(TestRepo.PathOf("src/Barbican.App/Assets/mail/shell.js"))));

    public static TheoryData<string, string> Forbidden => new()
    {
        { "location assignment", @"(^|[^\w.])location\s*=(?!=)" },
        { "location.href assignment", @"location\s*\.\s*href\s*=(?!=)" },
        { "document.location", @"document\s*\.\s*location" },
        { "window.location", @"window\s*\.\s*location" },
        { "top/parent location", @"(top|parent|opener)\s*\.\s*location" },
        { "location.assign", @"location\s*\.\s*assign\s*\(" },
        { "location.replace", @"location\s*\.\s*replace\s*\(" },
        { "location.reload", @"location\s*\.\s*reload\s*\(" },
        { "window.open", @"\bopen\s*\(" },
        { "document.write", @"\.\s*write(ln)?\s*\(" },
        { "history navigation", @"\bhistory\s*\.\s*(go|back|forward|pushState|replaceState)\b" },
        { "eval", @"\beval\s*\(|new\s+Function\s*\(" },
        { "string timers", @"set(Timeout|Interval)\s*\(\s*['""]" },
        { "frame navigation by src", @"\.\s*src\s*=(?!=)" },
        { "allow-scripts", "allow-scripts" },
        { "allow-top-navigation", "allow-top-navigation" },
        { "allow-popups", "allow-popups" },
        { "allow-forms", "allow-forms" },
        { "allow-downloads", "allow-downloads" },
    };

    [Theory]
    [MemberData(nameof(Forbidden))]
    public void ShellNeverNavigatesOrWidensTheSandbox(string what, string pattern)
    {
        var hit = Regex.Match(Code.Value, pattern, RegexOptions.Multiline, TimeSpan.FromSeconds(5));
        Assert.False(hit.Success, $"shell.js: {what} at \"{(hit.Success ? Around(hit.Index) : string.Empty)}\"");
    }

    [Fact]
    public void AFrameDocumentIsReadOnlyThroughThePrototypes()
    {
        // Fable review 2 R2-04: a mail element named URL, body, querySelectorAll ... shadows that member of the frame document
        var code = Code.Value;
        Assert.DoesNotMatch(@"(?<![\w$])d\s*\.\s*(URL|body|documentElement|defaultView|readyState|querySelectorAll|addEventListener|__bb\w*)(?![\w$])", code);
        Assert.Contains("Object.getOwnPropertyDescriptor(DP, n)", code, StringComparison.Ordinal);
        Assert.Contains("EventTarget.prototype.addEventListener.call(", code, StringComparison.Ordinal);
        Assert.Contains("new WeakSet()", code, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyAFinishedFrameIsRevealed()
    {
        // the host's give-up path must not show a frame the shell never handled (no link handler, no dark pass)
        var code = Code.Value;
        Assert.Matches(@"finished\.add\(f\);", code);
        Assert.Matches(@"reveal:\s*function\s*\(s(,\s*ms)?\)\s*\{\s*if\s*\(s !== seq \|\| !nxt \|\| !finished\.has\(nxt\)\)\s*return;", code);
    }

    [Fact]
    public void TheCrossFadeKeepsTheOldPageUnderTheNewOneUntilItEnds()
    {
        // docs/milestones/M1-look-gap.md: the incoming page fades in over the one on screen; the old one goes only after the fade
        // (or at once when another reveal, a clear or no fade comes), so nothing blank shows in between
        var code = Code.Value;
        Assert.Matches(@"if\s*\(cur && ms > 0\)", code);
        Assert.Matches(@"old = cur;\s*oldTimer = setTimeout\(dropOld, ms \+ 20\);", code);
        Assert.Matches(@"function dropOld\(\)", code);
    }

    [Fact]
    public void ReadyIsPostedOneFrameAfterTheLayoutWithATimerFallback()
    {
        // data: images may decode a frame late; a hidden window may never run a frame, so the timer keeps the page from waiting for good
        var code = Code.Value;
        Assert.Matches(@"requestAnimationFrame\(ready\);\s*setTimeout\(ready,\s*\d+\);", code);
        Assert.DoesNotMatch(@"offsetHeight;\s*post\('ready:'", code);
    }

    [Fact]
    public void EveryFrameGetsASandboxFromTheAllowListOnly()
    {
        var code = Code.Value;
        // The attribute is set unconditionally (the Rust shell skipped it for a null sandbox).
        Assert.Matches(@"f\.setAttribute\('sandbox',\s*sandbox\(sb\)\);", code);
        Assert.DoesNotMatch(@"if\s*\(\s*sb\s*!==?\s*null\s*\)", code);
        var list = SandboxList().Match(code);
        Assert.True(list.Success, "SANDBOX_OK not found in shell.js");
        var tokens = Regex.Matches(list.Groups["list"].Value, "'(?<t>[a-z-]+)'", RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1)).Select(m => m.Groups["t"].Value).ToArray();
        Assert.Equal(MailSandbox.Allowed, tokens);
    }

    [Fact]
    public void CoreSandboxUsesOnlyAllowedKeywordsAndNeverScripts()
    {
        var tokens = MailSandbox.Frame.Split(' ');
        Assert.All(tokens, t => Assert.Contains(t, MailSandbox.Allowed, StringComparer.Ordinal));
        Assert.DoesNotContain("allow-scripts", MailSandbox.Allowed, StringComparer.Ordinal);
        Assert.DoesNotContain(tokens, t => t.StartsWith("allow-top-navigation", StringComparison.Ordinal) || t.StartsWith("allow-popups", StringComparison.Ordinal));
    }

    [Fact]
    public void Only_the_frames_own_srcdoc_is_finished_and_the_shell_runs_the_passes()
    {
        // Fable F-03: the load path and the poll both go through finish(), which checks about:srcdoc; the passes run from the shell
        var code = Code.Value;
        Assert.Contains("G(d, 'url') !== 'about:srcdoc'", code, StringComparison.Ordinal);
        Assert.Matches(@"addEventListener\('load',[^\n]*finish\(d\)", code);
        Assert.Matches(@"size\(d, sz\);\s*if \(dk\) dark\(d\);", code);
        Assert.Contains("window.__bbDarkPass(d)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void FramesRouteLinksThroughACapturingClickHandler()
    {
        var code = Code.Value;
        Assert.Contains("on(d, 'click', link, true);", code, StringComparison.Ordinal);
        Assert.Contains("on(d, 'auxclick', link, true);", code, StringComparison.Ordinal);
        Assert.Contains("post('link:' + href)", code, StringComparison.Ordinal);
        Assert.Matches(@"function link\(e\)\s*\{[^}]*?e\.preventDefault\(\);", code);
    }

    [Fact]
    public void MessagesToTheHostAreOnlyTheKnownOnes()
    {
        var posts = Regex.Matches(Code.Value, @"post\('(?<name>[a-z]+)(?<colon>:?)'", RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1))
            .Select(m => m.Groups["name"].Value + m.Groups["colon"].Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["ctx:", "dirty", "key:", "link:", "ready:", "shell"], posts);
        // Every one of them passes the host's gate in its simplest form.
        foreach (var sample in new[] { "ctx:1,2", "dirty", "key:|j", "link:https://example.org/", "ready:1", "shell" })
        {
            Assert.True(ShellMessage.TryParse(sample, out _), sample);
        }
    }

    [Fact]
    public void CommentStripperKeepsCodeAndDropsComments()
    {
        var s = StripComments("a = 1; // location = x\n/* window.open() */ b = 'not // a comment'; c = /x\\/y/;");
        Assert.DoesNotContain("location", s, StringComparison.Ordinal);
        Assert.DoesNotContain("window.open", s, StringComparison.Ordinal);
        Assert.Contains("'not // a comment'", s, StringComparison.Ordinal);
    }

    // Removes // and /* */ comments outside string literals, so the checks see code only.
    private static string StripComments(string js)
    {
        var sb = new System.Text.StringBuilder(js.Length);
        var i = 0;
        while (i < js.Length)
        {
            var c = js[i];
            if (c is '\'' or '"')
            {
                var start = i++;
                while (i < js.Length && js[i] != c)
                {
                    i += js[i] == '\\' ? 2 : 1;
                }

                i = Math.Min(i + 1, js.Length);
                sb.Append(js, start, i - start);
            }
            else if (c == '/' && i + 1 < js.Length && js[i + 1] == '/')
            {
                while (i < js.Length && js[i] != '\n')
                {
                    i++;
                }
            }
            else if (c == '/' && i + 1 < js.Length && js[i + 1] == '*')
            {
                var end = js.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? js.Length : end + 2;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }

        return sb.ToString();
    }

    private static string Around(int index)
    {
        var code = Code.Value;
        var start = Math.Max(0, index - 30);
        return code.Substring(start, Math.Min(80, code.Length - start)).Replace('\n', ' ');
    }

    [GeneratedRegex(@"var SANDBOX_OK = \[(?<list>[^\]]*)\];", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SandboxList();
}
