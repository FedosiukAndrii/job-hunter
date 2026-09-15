namespace JobHunter.Domain.Jobs;

public readonly record struct JobScore
{
    public JobScore(int value)
    {
        if (value is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "A job score must be between 0 and 100.");
        }

        Value = value;
    }

    public int Value { get; }

    public static implicit operator int(JobScore score) => score.Value;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
