namespace Barbican.Spike.BodyShell;

/// <summary>Yes/no probes: the WebView APIs the shell needs, remote-content blocking, script blocking, navigation blocking.</summary>
internal sealed partial class Scenario
{
    private async Task ProbeApisAsync()
    {
        string? two = null;
        try
        {
            two = await _shell.EvalAsync("1+1");
        }
        catch (Exception e)
        {
            two = e.GetType().Name + ": " + e.Message;
        }

        Check("ExecuteScriptAsync returns a result", two == "2", $"\"1+1\" -> {two}");

        var ms = await ShowAsync(MailDocument.Solid(ColorA, "A (script transport)"), ColorA, false, ShellHost.Transport.Script, A, "A");
        Check("srcdoc frame ready:N via ExecuteScriptAsync show()", ms is not null, ms is null ? "no ready within 10 s" : $"{ms:F0} ms to ready");

        var big = MailDocument.Solid(ColorB, "B (web message, 3 MB) " + new string('x', 3_000_000));
        double? msBig = null;
        string detail;
        try
        {
            msBig = await ShowAsync(big, ColorB, false, ShellHost.Transport.Message, B, "B");
            detail = msBig is null ? "no ready within 10 s" : $"{big.Length / 1e6:F1} MB page, {msBig:F0} ms to ready";
        }
        catch (Exception e)
        {
            detail = e.GetType().Name + ": " + e.Message;
        }

        Check("PostWebMessageAsJson show() of a page over NavigateToString's 2 MB", msBig is not null, detail);

        await BeaconProbesAsync();
        await ScriptProbesAsync();
        await NavigationProbesAsync();
    }

    private async Task BeaconProbesAsync()
    {
        using var beacon = new BeaconServer();
        var p = beacon.Port;
        var remote = $"<img src=\"http://127.0.0.1:{p}/img.png\" width=10 height=10>" +
            $"<link rel=stylesheet href=\"http://127.0.0.1:{p}/s.css\">" +
            $"<style>@font-face{{font-family:bx;src:url(http://127.0.0.1:{p}/f.woff)}}p{{font-family:bx}}div.bg{{height:20px;background-image:url(http://127.0.0.1:{p}/bg.png)}}</style>" +
            $"<p>remote content probe</p><div class=bg></div><video poster=\"http://127.0.0.1:{p}/poster.png\"></video>";

        // 1. CSP alone (the resource filter lets everything through): nothing may reach the server.
        _shell.AllowRemote = true;
        await ShowAsync(Page(remote, images: false), "#ffffff", false, ShellHost.Transport.Script, A, "csp");
        await Task.Delay(1500);
        var cspHits = beacon.Hits.Count;
        Check("CSP blocks remote img/css/font/background/poster with images off", cspHits == 0, $"{cspHits} requests reached the beacon: {string.Join(", ", beacon.Hits)}");

        // 2. The resource filter alone (Windows): the CSP allows images, the filter must refuse them.
        beacon.Reset();
        _shell.AllowRemote = false;
        var filteredBefore = _shell.FilteredRemote;
        await ShowAsync(Page(remote, images: true), "#ffffff", false, ShellHost.Transport.Script, A, "filter");
        await Task.Delay(1500);
        if (_shell.ResourceFilterInstalled)
        {
            Check("WebResourceRequested filter blocks remote loads the CSP allows (defence in depth)", beacon.Hits.Count == 0 && _shell.FilteredRemote > filteredBefore, $"{beacon.Hits.Count} reached the beacon, {_shell.FilteredRemote - filteredBefore} refused by the filter");
        }
        else
        {
            _report.Notes.Add($"no WebResourceRequested filter on this head (remote blocking is by CSP only); with images allowed {beacon.Hits.Count} requests reached the beacon");
        }

        // 3. Both allow: the probe itself must see the requests, or the zeros above prove nothing.
        beacon.Reset();
        _shell.AllowRemote = true;
        await ShowAsync(Page(remote, images: true), "#ffffff", false, ShellHost.Transport.Script, A, "allowed");
        await Task.Delay(1500);
        Check("beacon sees remote loads when images are allowed (probe sanity)", beacon.Hits.Count > 0, $"{beacon.Hits.Count} requests: {string.Join(", ", beacon.Hits)}");
        _shell.AllowRemote = false;
    }

    private async Task ScriptProbesAsync()
    {
        const string Evil = "<script>parent.chrome.webview.postMessage('evil-inline')</script>" +
            "<img src=\"data:,\" onerror=\"parent.chrome.webview.postMessage('evil-handler')\">" +
            "<a id=x href=\"javascript:parent.chrome.webview.postMessage('evil-href')\">x</a>";

        // A frame without our script: sandbox without allow-scripts and script-src 'none'.
        await ShowAsync(Page(Evil, images: false), "#ffffff", false, ShellHost.Transport.Script, A, "evil1");
        await _shell.EvalAsync("(function(){try{var f=document.querySelectorAll('iframe');f[f.length-1].contentDocument.getElementById('x').click()}catch(e){}})()");
        await Task.Delay(1000);
        Check("mail scripts, inline handlers and javascript: links do not run (sandbox + CSP)", !SawMessage("evil"), SawMessage("evil") ? "a mail script ran" : "no script of the mail ran");

        // A frame with our hashed script (sandbox allows scripts): an unhashed inline script in the mail is still refused.
        var dark = MailDocument.Dark("<div style=\"background:#fff;color:#111;padding:20px\">dark + injected script</div><script>parent.chrome.webview.postMessage('evil-csp')</script>", _darkJs);
        var ms = await ShowAsync(dark, MailDocument.DarkBackground, true, ShellHost.Transport.Script, A, "evil2");
        await Task.Delay(1000);
        Check("only the hashed script runs in a script-enabled frame", ms is not null && !SawMessage("evil-csp"), ms is null ? "dark page never became ready" : SawMessage("evil-csp") ? "the injected script ran" : $"dark pass ran (ready in {ms:F0} ms), injected script refused");
    }

    private async Task NavigationProbesAsync()
    {
        using var beacon = new BeaconServer();
        var before = _shell.Blocked.Count;
        await ShowAsync(Page($"<a id=l href=\"http://127.0.0.1:{beacon.Port}/link\">link</a>", images: false), "#ffffff", false, ShellHost.Transport.Script, A, "nav");
        await _shell.EvalAsync("(function(){var f=document.querySelectorAll('iframe');f[f.length-1].contentDocument.getElementById('l').click()})()");
        await Task.Delay(800);
        await _shell.EvalAsync($"location.href='http://127.0.0.1:{beacon.Port}/top'");
        await Task.Delay(800);
        var blocked = _shell.Blocked.Skip(before).ToList();
        var shellAlive = await _shell.EvalAsync("typeof window.__bb");
        Check("link clicks and top-level navigations are blocked (routed, not loaded)", beacon.Hits.Count == 0 && shellAlive == "\"object\"", $"blocked: [{string.Join("; ", blocked)}], beacon hits {beacon.Hits.Count}, shell still loaded: {shellAlive}");
    }

    private static string Page(string body, bool images) =>
        $"<!doctype html><html><head><meta charset=utf-8>{MailDocument.Csp(images, null)}<base target=_blank><style>body{{margin:16px;font:14px sans-serif;color:#111;background:#fff}}</style></head><body>{body}</body></html>";
}
