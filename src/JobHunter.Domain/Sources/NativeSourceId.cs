namespace JobHunter.Domain.Sources;

public readonly record struct NativeSourceId
{
    private NativeSourceId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static NativeSourceId Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var normalized = value.Trim();
        if (normalized.Length > 512 || normalized.Any(char.IsControl))
        {
            throw new ArgumentException(
                "A native source ID must contain at most 512 non-control characters.",
                nameof(value));
        }

        return new NativeSourceId(normalized);
    }

    public override string ToString() => Value;
}
