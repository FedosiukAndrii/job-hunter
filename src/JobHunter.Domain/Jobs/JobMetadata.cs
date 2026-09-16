namespace JobHunter.Domain.Jobs;

public enum WorkplaceMode
{
    Unknown = 0,
    Remote = 1,
    Hybrid = 2,
    OnSite = 3
}

public enum EmploymentType
{
    Unknown = 0,
    FullTime = 1,
    PartTime = 2,
    Contract = 3,
    Temporary = 4,
    Internship = 5
}

public enum PublishedAtPrecision
{
    Unknown = 0,
    Date = 1,
    DateTime = 2
}

public enum CompensationPeriod
{
    Unknown = 0,
    Hour = 1,
    Month = 2,
    Year = 3
}
