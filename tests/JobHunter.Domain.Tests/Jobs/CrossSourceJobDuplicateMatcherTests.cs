using JobHunter.Domain.Jobs;

namespace JobHunter.Domain.Tests.Jobs;

public sealed class CrossSourceJobDuplicateMatcherTests
{
    [Fact]
    public void MatchNormalizesCasePunctuationAndWhitespace()
    {
        var firstPublishedAtUtc = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);

        var isMatch = CrossSourceJobDuplicateMatcher.IsMatch(
            " Acme, Inc. ",
            "Senior .NET Developer",
            firstPublishedAtUtc,
            "ACME INC",
            " senior NET   developer ",
            firstPublishedAtUtc.AddDays(5));

        Assert.True(isMatch);
        Assert.Equal(
            CrossSourceJobDuplicateMatcher.CreateMatchKey("Acme, Inc.", "Senior .NET Developer"),
            CrossSourceJobDuplicateMatcher.CreateMatchKey("ACME INC", "senior NET developer"));
    }

    [Fact]
    public void MatchRequiresBothPublishedDatesWithinSevenDays()
    {
        var firstPublishedAtUtc = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);

        Assert.False(CrossSourceJobDuplicateMatcher.IsMatch(
            "Acme",
            "Senior .NET Developer",
            firstPublishedAtUtc,
            "Acme",
            "Senior .NET Developer",
            null));
        Assert.False(CrossSourceJobDuplicateMatcher.IsMatch(
            "Acme",
            "Senior .NET Developer",
            firstPublishedAtUtc,
            "Acme",
            "Senior .NET Developer",
            firstPublishedAtUtc.AddDays(7).AddTicks(1)));
    }
}
