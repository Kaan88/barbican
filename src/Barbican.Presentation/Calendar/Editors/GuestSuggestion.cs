using Barbican.Core.Calendar.Editing;
namespace Barbican.Presentation.Calendar.Editors;

/// <summary>A contact suggestion under the guest field: a person, or a group with its members.</summary>
public sealed record GuestSuggestion(string Label, string Detail, IReadOnlyList<CalendarPerson> People);
