using System.Security.Cryptography;
using System.Text;

namespace JobHunter.Domain.Jobs;

public static class JobFingerprint
{
    public const int Version = 1;

    public static string Create(
        string title,
        string company,
        WorkplaceMode workplaceMode,
        IEnumerable<string> locations,
        EmploymentType employmentType,
        string descriptionText)
    {
        ArgumentNullException.ThrowIfNull(locations);

        var locationValue = string.Join(
            '|',
            locations
                .Select(Normalize)
                .Where(value => value.Length > 0)
                .Order(StringComparer.Ordinal));
        var descriptionExcerpt = Normalize(descriptionText);
        if (descriptionExcerpt.Length > 512)
        {
            descriptionExcerpt = descriptionExcerpt[..512];
        }

        var input = string.Join(
            '\n',
            $"v{Version}",
            Normalize(title),
            Normalize(company),
            workplaceMode.ToString(),
            locationValue,
            employmentType.ToString(),
            descriptionExcerpt);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return $"v{Version}:{Convert.ToHexStringLower(hash)}";
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var previousWasSpace = false;

        foreach (var character in value.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsLetterOrDigit(character) || character is '+' or '#' or '.')
            {
                builder.Append(char.ToLowerInvariant(character));
                previousWasSpace = false;
            }
            else if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }
}
