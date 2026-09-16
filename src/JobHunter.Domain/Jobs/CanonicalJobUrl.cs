namespace JobHunter.Domain.Jobs;

public sealed record CanonicalJobUrl
{
    private CanonicalJobUrl(Uri value)
    {
        Value = value;
    }

    public Uri Value { get; }

    public static CanonicalJobUrl Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(parsed.Host)
            || !string.IsNullOrEmpty(parsed.UserInfo))
        {
            throw new ArgumentException(
                "A canonical job URL must be an absolute HTTP or HTTPS URL without credentials.",
                nameof(value));
        }

        var builder = new UriBuilder(parsed)
        {
            Fragment = string.Empty,
            Host = parsed.IdnHost.ToLowerInvariant(),
            Scheme = parsed.Scheme.ToLowerInvariant(),
            Query = FilterTrackingParameters(parsed.Query)
        };

        if ((builder.Scheme == Uri.UriSchemeHttp && builder.Port == 80)
            || (builder.Scheme == Uri.UriSchemeHttps && builder.Port == 443))
        {
            builder.Port = -1;
        }

        return new CanonicalJobUrl(builder.Uri);
    }

    public override string ToString() => Value.AbsoluteUri;

    private static string FilterTrackingParameters(string query)
    {
        if (query.Length <= 1)
        {
            return string.Empty;
        }

        var retainedParameters = query[1..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(parameter => !IsTrackingParameter(parameter))
            .OrderBy(GetParameterName, StringComparer.Ordinal)
            .ThenBy(parameter => parameter, StringComparer.Ordinal);

        return string.Join('&', retainedParameters);
    }

    private static string GetParameterName(string parameter)
    {
        var separatorIndex = parameter.IndexOf('=');
        return separatorIndex < 0 ? parameter : parameter[..separatorIndex];
    }

    private static bool IsTrackingParameter(string parameter)
    {
        var encodedName = GetParameterName(parameter);
        var name = Uri.UnescapeDataString(encodedName.Replace('+', ' '));

        return name.Equals("from", StringComparison.OrdinalIgnoreCase)
            || name.Equals("lipi", StringComparison.OrdinalIgnoreCase)
            || name.Equals("midSig", StringComparison.OrdinalIgnoreCase)
            || name.Equals("midToken", StringComparison.OrdinalIgnoreCase)
            || name.Equals("refId", StringComparison.OrdinalIgnoreCase)
            || name.Equals("trackingId", StringComparison.OrdinalIgnoreCase)
            || name.Equals("trk", StringComparison.OrdinalIgnoreCase)
            || name.Equals("trkInfo", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("utm_", StringComparison.OrdinalIgnoreCase);
    }
}
