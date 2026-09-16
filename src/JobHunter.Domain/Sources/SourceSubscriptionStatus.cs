namespace JobHunter.Domain.Sources;

public enum SourceSubscriptionStatus
{
    Enabled = 0,
    Running = 1,
    BackingOff = 2,
    Blocked = 3,
    Disabled = 4
}
