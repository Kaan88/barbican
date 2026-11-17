#if AUTOMATION
// Mirrors: M2 plan §6.3 hidden-automation scenario ("rx-banners" for each seed message, "banner-act one-click"); spec/automation-protocol.md
// (M2 reader rows). Reads the banner stack of the message on screen; "act" runs one banner button through ReaderBannerActions (the browser
// launcher of an Automation build refuses, and production GuardedHttp refuses loopback and plain http, so nothing leaves the machine). Owner reader.

using Barbican.Automation;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Reader;

namespace Barbican.App.MailBody.Automation;

/// <summary>The <c>rx-banners</c> command: <c>list</c> (default) or <c>act ACTION</c>.</summary>
public sealed class RxBannersCommand(ReaderViewModel reader, ReaderBannerActions actions) : IAutomationCommand
{
    public string Name => "rx-banners";

    public async Task<string> RunAsync(string args, CancellationToken cancellationToken)
    {
        var a = (args ?? string.Empty).Trim();
        if (a.Length == 0 || string.Equals(a, "list", StringComparison.Ordinal))
        {
            var items = reader.Banners.Select(b => $"{b.Id}[{string.Join(",", b.Actions.Select(x => x.ActionId))}]{(b.Note is null ? string.Empty : "+note")}{(b.IsWarning ? "!" : string.Empty)}");
            return $"banners {reader.EmailId} {reader.Banners.Count} {string.Join(" ", items)}".TrimEnd();
        }

        if (a.StartsWith("act ", StringComparison.Ordinal))
        {
            var result = await actions.RunAsync(reader.EmailId, a[4..].Trim(), cancellationToken).ConfigureAwait(true);
            return "acted " + result;
        }

        return "error rx-banners list|act ACTION";
    }
}
#endif
