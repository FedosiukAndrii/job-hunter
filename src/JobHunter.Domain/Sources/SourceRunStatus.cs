namespace JobHunter.Domain.Sources;

public enum SourceRunStatus
{
    Running = 0,
    Succeeded = 1,
    Partial = 2,
    Blocked = 3,
    Failed = 4
}
