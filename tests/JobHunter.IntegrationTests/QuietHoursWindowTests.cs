using JobHunter.Worker;
using System.Globalization;

namespace JobHunter.IntegrationTests;

public sealed class QuietHoursWindowTests
{
    [Fact]
    public void DefaultsUseUkraineTimeZone()
    {
        Assert.Equal("Europe/Kyiv", new WorkerQuietHoursOptions().TimeZoneId);
    }

    [Theory]
    [InlineData("2026-09-21T21:59:59Z", false)]
    [InlineData("2026-09-21T22:00:00Z", true)]
    [InlineData("2026-09-22T00:00:00Z", true)]
    [InlineData("2026-09-22T08:59:59Z", true)]
    [InlineData("2026-09-22T09:00:00Z", false)]
    public void CrossMidnightWindowUsesInclusiveStartAndExclusiveEnd(
        string utcNow,
        bool expected)
    {
        var window = new QuietHoursWindow(
            new WorkerQuietHoursOptions
            {
                StartLocalTime = new TimeOnly(22, 0),
                EndLocalTime = new TimeOnly(9, 0),
                TimeZoneId = "UTC"
            });

        Assert.Equal(
            expected,
            window.IsQuiet(
                DateTimeOffset.Parse(utcNow, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void DisabledWindowNeverPausesWork()
    {
        var window = new QuietHoursWindow(
            new WorkerQuietHoursOptions
            {
                Enabled = false,
                TimeZoneId = "UTC"
            });

        Assert.False(
            window.IsQuiet(
                DateTimeOffset.Parse(
                    "2026-09-21T23:00:00Z",
                    CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void InvalidTimeZoneIsRejected()
    {
        Assert.False(QuietHoursWindow.HasValidTimeZoneId("Not/A-Time-Zone"));
    }
}
