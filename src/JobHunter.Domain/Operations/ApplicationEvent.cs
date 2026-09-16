namespace JobHunter.Domain.Operations;

public sealed class ApplicationEvent
{
    private ApplicationEvent()
    {
    }

    public Guid Id { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    public string Severity { get; private set; } = string.Empty;

    public string Message { get; private set; } = string.Empty;

    public string? PropertiesJson { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
