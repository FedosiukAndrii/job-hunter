namespace JobHunter.Domain.Jobs;

public enum JobLifecycle
{
    Active = 0,
    PossiblyStale = 1,
    Expired = 2,
    Removed = 3
}
