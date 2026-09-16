using JobHunter.Domain.Sources;

namespace JobHunter.Domain.Jobs;

public readonly record struct JobKey
{
    private JobKey(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static JobKey Create(
        SourceName source,
        NativeSourceId? sourceJobId,
        CanonicalJobUrl canonicalUrl)
    {
        var identity = sourceJobId is null
            ? $"url:{canonicalUrl}"
            : $"id:{sourceJobId.Value}";

        return new JobKey($"{source.Value}:{identity}");
    }

    public override string ToString() => Value;
}
