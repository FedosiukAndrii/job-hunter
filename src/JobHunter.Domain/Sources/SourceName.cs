namespace JobHunter.Domain.Sources;

public readonly record struct SourceName
{
    private SourceName(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static SourceName Dou { get; } = new("dou");

    public static SourceName LinkedInJobSpy { get; } = new("linkedin-jobspy");

    public static SourceName Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > 64
            || normalized.Any(character =>
                !(character is >= 'a' and <= 'z'
                    or >= '0' and <= '9'
                    or '-')))
        {
            throw new ArgumentException(
                "A source name must contain at most 64 lowercase ASCII letters, digits, or hyphens.",
                nameof(value));
        }

        return new SourceName(normalized);
    }

    public override string ToString() => Value;
}
