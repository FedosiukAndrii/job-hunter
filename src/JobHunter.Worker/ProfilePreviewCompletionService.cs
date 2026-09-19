using JobHunter.Application.Profiles;

namespace JobHunter.Worker;

internal sealed class ProfilePreviewCompletionService(
    ICandidateProfileLoader profileLoader,
    IHostApplicationLifetime applicationLifetime)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var profile = await profileLoader.LoadAsync(cancellationToken);

        Console.Out.WriteLine("Structured profile supplied to evaluation:");
        Console.Out.WriteLine(profile.CanonicalJson);
        Console.Out.WriteLine();
        Console.Out.WriteLine("Supplemental CV evidence supplied to AI (redacted):");
        Console.Out.WriteLine(profile.SupplementalCvRedacted ?? "(none)");
        applicationLifetime.StopApplication();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
