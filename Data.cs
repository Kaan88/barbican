using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;

namespace Barbican;

public sealed class Mail : INotifyPropertyChanged
{
    public string From { get; init; }
    public string Email { get; init; }
    public string Subject { get; init; }
    public string Snippet { get; init; }
    public string Time { get; init; }
    public string Folder { get; set; } = "Inbox";
    public bool Unread { get; set; }
    public int Thread { get; init; } = 1;
    public string Html { get; init; }
    public string Plain { get; init; }
    public int Tint { get; init; }
    public string Org { get; init; }
    public string Label { get; init; }

    public string Initials => string.Concat(From.Split(' ').Where(w => w.Length > 0).Select(w => w[0])).Substring(0, System.Math.Min(2, From.Split(' ').Length));
    static readonly string[] Tints = { "Rose", "Sky", "Mint", "Amber" };
    public Brush AvatarBg => Palette.B(Tints[Tint % 4] + "Bg");
    public Brush AvatarFg => Palette.B(Tints[Tint % 4] + "Fg");
    public FontWeight FromWeight => Unread ? FontWeights.Bold : FontWeights.SemiBold;
    public FontWeight NordWeight => Unread ? FontWeights.Bold : FontWeights.Medium;
    public Brush DotBrush => Palette.B(Unread ? "Accent" : "Clear");
    public Brush TimeBrush => Palette.B(Unread ? "Accent" : "Faint");
    public Visibility DotVis => Unread ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ThreadVis => Thread > 1 ? Visibility.Visible : Visibility.Collapsed;
    public string ThreadText => Thread.ToString();
    public CornerRadius AvatarRadius => Palette.Nordic ? new CornerRadius(19) : new CornerRadius(13);

    public event PropertyChangedEventHandler PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}

public static class Sample
{
    public static readonly (string Name, string Icon)[] Folders =
    {
        ("Inbox", "inbox"), ("Drafts", "file"), ("Scheduled", "clock"), ("Sent", "send"), ("Archive", "archive"), ("Spam", "alert"), ("Trash", "trash"),
    };

    public static string NewsletterHtml => Newsletter;
    const string Newsletter = """
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#efe9df;padding:28px 0;font-family:Georgia,serif">
<tr><td align="center">
<table role="presentation" width="560" cellpadding="0" cellspacing="0" style="max-width:560px;background:#fffdf8;border-radius:6px;overflow:hidden">
<tr><td style="background:#1f2a24;color:#f3efe6;padding:26px 32px">
<div style="font:600 11px/1 Helvetica,Arial,sans-serif;letter-spacing:.18em;text-transform:uppercase;opacity:.7">The Browser · Issue 1,482</div>
<div style="font-size:30px;line-height:1.15;margin-top:10px">Five essays worth your weekend</div></td></tr>
<tr><td style="padding:26px 32px 6px;color:#2a2622;font-size:17px;line-height:1.6">
<p style="margin:0 0 18px">Good morning. This week: on slowness, on maps that lie on purpose, and on the strange afterlife of the fax machine.</p>
<h3 style="font:700 13px Helvetica,Arial,sans-serif;letter-spacing:.08em;text-transform:uppercase;color:#a0522d;margin:24px 0 6px">1 · In Praise of Slowness</h3>
<p style="margin:0 0 14px">A cabinet-maker explains why the last two percent of a job takes half the time, and why that is the part people remember.</p>
<h3 style="font:700 13px Helvetica,Arial,sans-serif;letter-spacing:.08em;text-transform:uppercase;color:#a0522d;margin:24px 0 6px">2 · Every Map Is an Argument</h3>
<p style="margin:0 0 14px">Projections, paper towns and the cartographers who hid jokes in the Alps.</p>
<h3 style="font:700 13px Helvetica,Arial,sans-serif;letter-spacing:.08em;text-transform:uppercase;color:#a0522d;margin:24px 0 6px">3 · The Fax Will Outlive Us All</h3>
<p style="margin:0 0 14px">Why hospitals in three countries still run on thermal paper.</p>
<p style="margin:26px 0"><a href="#" style="display:inline-block;background:#a0522d;color:#fff;text-decoration:none;font:600 14px Helvetica,Arial,sans-serif;padding:12px 22px;border-radius:999px">Read all five</a></p>
</td></tr>
<tr><td style="padding:18px 32px 26px;border-top:1px solid #e8e1d4;color:#8a8278;font:12px/1.5 Helvetica,Arial,sans-serif">You get this because you subscribed at thebrowser.com. <a href="#" style="color:#8a8278">Unsubscribe</a></td></tr>
</table></td></tr></table>
""";

    public static readonly List<Mail> All = new()
    {
        new() { From = "Mira Okafor", Email = "mira@okafor.studio", Org = "Okafor Studio · Design lead", Subject = "Design review moved to Thursday", Snippet = "Room 4B is booked from 14:00, I'll bring the printed boards…", Time = "09:42", Unread = true, Thread = 4, Tint = 0, Label = "Work",
            Html = "<p>Hi both,</p><p>Quick one: the design review moves to <b>Thursday 14:00</b>, room 4B. Facilities needed the big room on Wednesday.</p><ul><li>I'll bring the printed boards for Bento and Nordic</li><li>Jonas, can you have the numbers on one slide?</li><li>We stop at 15:30 sharp</li></ul><p>Thanks,<br>Mira</p>" },
        new() { From = "Stalwart Labs", Email = "news@stalw.art", Org = "Stalwart · Mail server", Subject = "Stalwart 0.16 is out", Snippet = "FileNode sharing, a faster full-text index and JMAP for Calendars…", Time = "08:15", Unread = true, Tint = 1, Label = "Work",
            Html = "<h2 style='margin-top:0'>Stalwart 0.16</h2><p>This release brings <b>FileNode sharing</b>, a rewritten full-text index (about 3× faster on large mailboxes) and JMAP for Calendars.</p><pre style='background:rgba(127,127,127,.12);padding:12px;border-radius:10px'>docker pull stalwartlabs/stalwart:v0.16</pre><p>Read the <a href='#'>changelog</a> for upgrade notes.</p>" },
        new() { From = "TAP Air Portugal", Email = "noreply@flytap.com", Org = "Airline", Subject = "Your boarding pass · LIS → AMS", Snippet = "Flight TP668 departs at 07:25 from Terminal 1, gate opens…", Time = "07:58", Unread = true, Tint = 1, Label = "Travel",
            Html = "<div style='border:1px solid rgba(127,127,127,.3);border-radius:16px;padding:20px;max-width:460px'><div style='font-size:12px;opacity:.7;letter-spacing:.1em'>BOARDING PASS</div><div style='display:flex;justify-content:space-between;font-size:34px;font-weight:700;margin:10px 0'><span>LIS</span><span>→</span><span>AMS</span></div><table style='width:100%;font-size:14px'><tr><td>Flight<br><b>TP668</b></td><td>Seat<br><b>14C</b></td><td>Boards<br><b>06:45</b></td><td>Gate<br><b>14</b></td></tr></table></div><p>Have a good flight.</p>" },
        new() { From = "Jonas Weber", Email = "jonas.weber@fastmail.de", Org = "Finance", Subject = "Re: Quarterly numbers", Snippet = "One question on the churn figure before I send it on…", Time = "Mon", Thread = 3, Tint = 2, Label = "Work",
            Plain = "Hi,\n\nOne question on the churn figure before I send it on: is the 2.1% monthly or quarterly?\nIf monthly, the board deck is off by a factor of three.\n\n> On Sun, Mira wrote:\n> Numbers attached, slide 4 is the new one.\n\nCheers,\nJonas\n\n-- \nJonas Weber · Finance" },
        new() { From = "Hetzner Online", Email = "billing@hetzner.com", Org = "Hosting", Subject = "Invoice 2026-11", Snippet = "Your invoice of €38.17 is available in the Robot panel…", Time = "Mon", Tint = 3, Label = "Receipts",
            Html = "<p>Dear customer,</p><p>your invoice <b>R0021938217</b> for November 2026 is now available.</p><table style='border-collapse:collapse;min-width:300px'><tr><td style='padding:6px 0'>CX32 · fsn1</td><td style='text-align:right'>€30.27</td></tr><tr><td style='padding:6px 0'>Storage Box BX11</td><td style='text-align:right'>€3.81</td></tr><tr><td style='padding:6px 0'>VAT</td><td style='text-align:right'>€4.09</td></tr><tr style='border-top:1px solid rgba(127,127,127,.4);font-weight:700'><td style='padding:8px 0'>Total</td><td style='text-align:right'>€38.17</td></tr></table><p>Kind regards<br>Hetzner Online GmbH</p>" },
        new() { From = "Ada Lindqvist", Email = "ada@lindqvist.example", Org = "Family", Subject = "Dinner on Sunday?", Snippet = "Mum says 7 is fine, bring the good bread from…", Time = "Fri", Tint = 3, Label = "Family",
            Plain = "Hey!\n\nMum says 7 is fine. Bring the good bread from the place on the corner, not the supermarket one :)\n\nAlso, can you fix her printer while you're there? It's \"offline\" again.\n\nx Ada" },
        new() { From = "The Browser", Email = "editors@thebrowser.com", Org = "Newsletter", Subject = "Five essays worth your weekend", Snippet = "On slowness, on maps, and on the fax machine…", Time = "Fri", Tint = 0, Html = Newsletter },
        new() { From = "GitHub", Email = "noreply@github.com", Org = "Notifications", Subject = "[barbican] PR #212: Bento list motion", Snippet = "mira requested your review on #212…", Time = "Thu", Thread = 6, Tint = 1, Label = "Work",
            Html = "<p><b>mira</b> requested your review on <a href='#'>#212 Bento list motion</a>.</p><blockquote style='border-left:3px solid rgba(127,127,127,.5);margin:0;padding:4px 12px;opacity:.85'>Rows now slide in with the incremental model; no full rebuild on refresh.</blockquote><p style='font-size:12px;opacity:.6'>Reply to this email directly or view it on GitHub.</p>" },
        new() { From = "Lena Fischer", Email = "lena@fischer-arch.de", Org = "Fischer Architekten", Subject = "Plans for the studio, v3", Snippet = "Attached the third round, the north wall now has…", Time = "Wed", Tint = 2,
            Html = "<p>Hello,</p><p>attached is the third round. The north wall now has the long window you asked for, and the stair moved 40 cm east.</p><p>Can we talk on Monday?</p><p>Lena</p>" },
        new() { From = "Mira Okafor", Email = "mira@okafor.studio", Org = "Okafor Studio · Design lead", Subject = "Draft: palette notes", Snippet = "Rose for today, mint for waiting…", Time = "Tue", Folder = "Drafts", Tint = 0,
            Plain = "Rose = today\nMint = waiting / done\nSky = travel / work\nAmber = attention\n\n(unfinished)" },
        new() { From = "Me", Email = "me@barbican.mail", Org = "", Subject = "Re: Design review moved to Thursday", Snippet = "Works for me, see you in 4B.", Time = "10:02", Folder = "Sent", Tint = 2,
            Plain = "Works for me, see you in 4B.\n\n> Quick one: the design review moves to Thursday 14:00" },
        new() { From = "Prize Centre", Email = "win@prize-centre.biz", Org = "", Subject = "You have (1) unclaimed reward", Snippet = "Click within 24 hours to claim…", Time = "Sun", Folder = "Spam", Tint = 3,
            Html = "<p style='font-size:22px'>Congratulations!!</p><p>Claim now.</p>" },
    };

    public static int Count(string folder) => folder == "Inbox" ? All.Count(m => m.Folder == folder && m.Unread) : All.Count(m => m.Folder == folder);
}

