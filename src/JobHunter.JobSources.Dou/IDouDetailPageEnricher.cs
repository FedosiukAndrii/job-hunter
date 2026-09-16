using JobHunter.JobSources.Dou.Parsing;

namespace JobHunter.JobSources.Dou;

public interface IDouDetailPageEnricher
{
    Task<SanitizedHtml?> FetchAsync(
        Uri detailPage,
        CancellationToken cancellationToken);
}
