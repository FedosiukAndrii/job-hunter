namespace JobHunter.Domain.Notifications;

public enum OutboxStatus
{
    Pending = 0,
    Leased = 1,
    Sent = 2,
    Unknown = 3,
    PermanentFailure = 4
}
