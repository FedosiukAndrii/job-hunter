namespace JobHunter.Domain.Sources;

public sealed class SourceCursor
{
    private SourceCursor()
    {
    }

    public Guid SourceSubscriptionId { get; private set; }

    public SourceSubscription SourceSubscription { get; private set; } = null!;

    public string? EntityTag { get; private set; }

    public DateTimeOffset? LastModifiedAtUtc { get; private set; }

    public string? OpaqueValue { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static SourceCursor Create(
        Guid sourceSubscriptionId,
        string? entityTag,
        DateTimeOffset? lastModifiedAtUtc,
        string? opaqueValue,
        DateTimeOffset now) =>
        new()
        {
            SourceSubscriptionId = sourceSubscriptionId,
            EntityTag = Bound(entityTag, 512),
            LastModifiedAtUtc = lastModifiedAtUtc,
            OpaqueValue = Bound(opaqueValue, 4096),
            UpdatedAtUtc = now
        };

    public void Update(
        string? entityTag,
        DateTimeOffset? lastModifiedAtUtc,
        string? opaqueValue,
        DateTimeOffset now)
    {
        EntityTag = Bound(entityTag, 512);
        LastModifiedAtUtc = lastModifiedAtUtc;
        OpaqueValue = Bound(opaqueValue, 4096);
        UpdatedAtUtc = now;
    }

    private static string? Bound(string? value, int maximumLength) =>
        value is null
            ? null
            : value.Length <= maximumLength
                ? value
                : value[..maximumLength];
}
