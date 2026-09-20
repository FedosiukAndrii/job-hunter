using System.Security.Cryptography;
using System.Text;

namespace JobHunter.Domain.Jobs;

public static class CrossSourceJobDuplicateMatcher
{
    public const string CompanyTitlePublishedAtV2 = "CompanyTitlePublishedAtV2";

    private static readonly TimeSpan MaximumPublishedAtDifference = TimeSpan.FromDays(7);

    public static bool IsMatch(
        string? leftCompany,
        string? leftTitle,
        DateTimeOffset? leftPublishedAtUtc,
        string? rightCompany,
        string? rightTitle,
        DateTimeOffset? rightPublishedAtUtc)
    {
        if (leftPublishedAtUtc is null || rightPublishedAtUtc is null)
        {
            return false;
        }

        var normalizedLeftCompany = Normalize(leftCompany);
        var normalizedRightCompany = Normalize(rightCompany);
        var normalizedLeftTitle = Normalize(leftTitle);
        var normalizedRightTitle = Normalize(rightTitle);

        return normalizedLeftCompany.Length > 0
            && normalizedLeftTitle.Length > 0
            && string.Equals(
                normalizedLeftCompany,
                normalizedRightCompany,
                StringComparison.Ordinal)
            && string.Equals(
                normalizedLeftTitle,
                normalizedRightTitle,
                StringComparison.Ordinal)
            && (leftPublishedAtUtc.Value - rightPublishedAtUtc.Value).Duration()
                <= MaximumPublishedAtDifference;
    }

    public static string CreateMatchKey(string? company, string? title)
    {
        var input = string.Join(
            '\n',
            "v2",
            Normalize(company),
            Normalize(title));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return $"v2:{Convert.ToHexStringLower(hash)}";
    }

    public static (DateTimeOffset Earliest, DateTimeOffset Latest) GetPublishedAtMatchWindow(
        DateTimeOffset publishedAtUtc) =>
        (
            publishedAtUtc.Subtract(MaximumPublishedAtDifference),
            publishedAtUtc.Add(MaximumPublishedAtDifference));

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var previousWasSpace = false;

        foreach (var character in value.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsLetterOrDigit(character) || character is '#' or '+')
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
