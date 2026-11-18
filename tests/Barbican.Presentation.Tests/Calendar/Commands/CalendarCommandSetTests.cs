using Barbican.Presentation.Calendar.Commands;
using Barbican.Presentation.Tests.Calendar.Editors;

namespace Barbican.Presentation.Tests.Calendar.Commands;

public sealed class CalendarCommandSetTests
{
    [Fact]
    public void The_menu_count_matches_the_providers_created()
    {
        Assert.Equal(CalendarMenus.Count, CalendarMenus.Create(new FakeSource()).Count);
    }
}
