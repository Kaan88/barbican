using Barbican.Core.Calendar.Editing;
using CommunityToolkit.Mvvm.ComponentModel;
namespace Barbican.Presentation.Calendar.Editors;

/// <summary>A guest chip: the person, the initials, their answer and their availability for the event window.</summary>
public sealed class GuestRow : ObservableObject
{
    private string _availability = string.Empty;
    private int _availabilityRank;

    public GuestRow(CalendarPerson person, string initials, string answer)
    {
        ArgumentNullException.ThrowIfNull(person);
        Person = person;
        Initials = initials;
        Answer = answer;
    }

    public CalendarPerson Person { get; }

    public string Initials { get; }

    /// <summary>"Accepted", "Declined", "Maybe" or "Awaiting reply" (empty for a new guest).</summary>
    public string Answer { get; }

    public string Display => Person.Name.Length > 0 ? Person.Name : Person.Email;

    /// <summary>"Free", "Tentative", "Busy" or a reason it is unknown; empty before it is read.</summary>
    public string Availability
    {
        get => _availability;
        set => SetProperty(ref _availability, value);
    }

    /// <summary>1 free, 2 tentative, 3 busy, 0 unknown.</summary>
    public int AvailabilityRank
    {
        get => _availabilityRank;
        set => SetProperty(ref _availabilityRank, value);
    }
}
