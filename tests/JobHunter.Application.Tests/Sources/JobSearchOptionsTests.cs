using JobHunter.Application.Sources;

namespace JobHunter.Application.Tests.Sources;

public sealed class JobSearchOptionsTests
{
    [Fact]
    public void DefaultsToTwentyFourHourLookback()
    {
        var options = new JobSearchOptions();

        Assert.Equal(24, options.LookbackHours);
    }
}
