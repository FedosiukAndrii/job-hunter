using JobHunter.Domain.Jobs;
using JobHunter.Domain.Sources;

namespace JobHunter.Domain.Tests.Sources;

public sealed class SourceIdentityTests
{
    [Fact]
    public void SourceNameNormalizesCaseAndWhitespace()
    {
        var source = SourceName.Create("  LINKEDIN-JobSpy ");

        Assert.Equal(SourceName.LinkedInJobSpy, source);
    }

    [Fact]
    public void JobKeyPrefersNativeIdOverCanonicalUrl()
    {
        var source = SourceName.Dou;
        var sourceId = NativeSourceId.Create("123");

        var first = JobKey.Create(
            source,
            sourceId,
            CanonicalJobUrl.Create("https://jobs.dou.ua/vacancies/123/?a=1"));
        var second = JobKey.Create(
            source,
            sourceId,
            CanonicalJobUrl.Create("https://jobs.dou.ua/vacancies/123/?a=2"));

        Assert.Equal(first, second);
    }
}
