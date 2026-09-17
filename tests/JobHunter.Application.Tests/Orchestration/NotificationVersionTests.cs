using JobHunter.Application.Orchestration;

namespace JobHunter.Application.Tests.Orchestration;

public sealed class NotificationVersionTests
{
    [Fact]
    public void NotificationVersionAdvancesWhenNotificationFormatChanges() =>
        Assert.Equal(2, ScanOrchestrator.NotificationVersion);
}
