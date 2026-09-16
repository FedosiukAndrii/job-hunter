using JobHunter.Domain.Jobs;

namespace JobHunter.Domain.Tests.Jobs;

public sealed class CanonicalJobUrlTests
{
    [Theory]
    [InlineData(
        "HTTPS://Jobs.DOU.UA:443/vacancies/123/?utm_source=feed&from=widget&search=dotnet#apply",
        "https://jobs.dou.ua/vacancies/123/?search=dotnet")]
    [InlineData(
        "https://jobs.dou.ua/vacancies/123?UTM_CAMPAIGN=test&language=C%23",
        "https://jobs.dou.ua/vacancies/123?language=C%23")]
    [InlineData(
        "http://jobs.dou.ua:80/vacancies/123?remote=true",
        "http://jobs.dou.ua/vacancies/123?remote=true")]
    [InlineData(
        "https://jobs.dou.ua/vacancies/123?z=last&utm_medium=rss&a=first",
        "https://jobs.dou.ua/vacancies/123?a=first&z=last")]
    [InlineData(
        "https://www.linkedin.com/jobs/view/123?trk=public_jobs&trackingId=opaque&refId=opaque&position=1",
        "https://www.linkedin.com/jobs/view/123?position=1")]
    public void CreateRemovesTrackingDataAndNormalizesAuthority(string source, string expected)
    {
        var result = CanonicalJobUrl.Create(source);

        Assert.Equal(expected, result.ToString());
    }

    [Theory]
    [InlineData("ftp://jobs.dou.ua/vacancies/123")]
    [InlineData("https://user:password@jobs.dou.ua/vacancies/123")]
    [InlineData("/vacancies/123")]
    [InlineData("not a URL")]
    public void CreateRejectsUnsafeOrRelativeUrls(string source)
    {
        Assert.Throws<ArgumentException>(() => CanonicalJobUrl.Create(source));
    }
}
