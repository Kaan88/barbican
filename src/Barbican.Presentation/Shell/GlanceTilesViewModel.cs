// Mirrors: Rust glance.rs (greeting :27, title :40, refresh :93: flagged count and who); M1-plan §4.3; spec B-MAIL-046. Owner mailcore.
// The calendar and task tiles belong to M4 (Rust glance.rs calendar :69); until then they report "no calendar".

using Barbican.Core.Mail.Glance;
using Barbican.Core.Mail.Model;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Shell;

/// <summary>The list header (folder title, unread count, greeting) and the Bento summary tiles. Everything comes from the account; nothing is made up.</summary>
public sealed class GlanceTilesViewModel : ObservableObject
{
    private readonly TimeProvider _time;
    private string _firstName = string.Empty;
    private string _listTitle = "Mail";
    private int _listUnread;
    private int _flaggedCount;
    private string _flaggedWho = string.Empty;
    private bool _hasFlagged;

    public GlanceTilesViewModel(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
    }

    /// <summary>The open folder's name.</summary>
    public string ListTitle => _listTitle;

    public int ListUnread => _listUnread;

    /// <summary>"Good morning, Kaan. 3 unread messages."</summary>
    public string ListSubtitle => GlanceRules.ListSubtitle(Greeting, _listUnread);

    public string Greeting => GlanceRules.Greeting(_time.GetLocalNow().Hour, _firstName);

    /// <summary>The flagged tile has data (the first answer arrived).</summary>
    public bool HasFlagged => _hasFlagged;

    public int FlaggedCount => _flaggedCount;

    /// <summary>The first names of the latest senders, "Ada, Lin".</summary>
    public string FlaggedWho => _flaggedWho;

    /// <summary>Today's events are known (M4 fills them; until then the tile is not shown).</summary>
    public bool HasCalendar { get; private set; }

    public int TodayCount { get; private set; }

    public string TodayNext { get; private set; } = string.Empty;

    /// <summary>Open tasks are known (M4).</summary>
    public bool HasTasks { get; private set; }

    public int TaskCount { get; private set; }

    public string TaskNext { get; private set; } = string.Empty;

    /// <summary>The calendar's numbers for the Today and To do tiles (called by the calendar once it has loaded).</summary>
    public void SetCalendar(int todayCount, string todayNext, int taskCount, string taskNext)
    {
        HasCalendar = true;
        HasTasks = true;
        TodayCount = todayCount;
        TodayNext = todayNext ?? string.Empty;
        TaskCount = taskCount;
        TaskNext = taskNext ?? string.Empty;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>The user's first name, from the sending identities.</summary>
    public void SetIdentities(IReadOnlyList<Identity> identities)
    {
        _firstName = GlanceRules.FirstName(identities);
        OnPropertyChanged(nameof(Greeting));
        OnPropertyChanged(nameof(ListSubtitle));
    }

    /// <summary>The open folder and its unread count.</summary>
    public void SetFolder(string name, int unread)
    {
        _listTitle = string.IsNullOrEmpty(name) ? "Mail" : name;
        _listUnread = unread;
        OnPropertyChanged(nameof(ListTitle));
        OnPropertyChanged(nameof(ListUnread));
        OnPropertyChanged(nameof(ListSubtitle));
    }

    /// <summary>Flagged mail: how many, and who sent the latest (newest first).</summary>
    public void SetFlagged(int total, IReadOnlyList<EmailSummary> latestNewestFirst)
    {
        ArgumentNullException.ThrowIfNull(latestNewestFirst);
        _hasFlagged = true;
        _flaggedCount = total;
        _flaggedWho = GlanceRules.FlaggedWho(latestNewestFirst);
        OnPropertyChanged(nameof(HasFlagged));
        OnPropertyChanged(nameof(FlaggedCount));
        OnPropertyChanged(nameof(FlaggedWho));
    }
}
