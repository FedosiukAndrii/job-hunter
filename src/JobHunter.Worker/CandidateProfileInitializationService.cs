using JobHunter.Application.Profiles;

namespace JobHunter.Worker;

internal sealed partial class CandidateProfileInitializationService(
    ICandidateProfileLoader profileLoader,
    ICandidateProfileStore profileStore,
    TimeProvider timeProvider,
    ILogger<CandidateProfileInitializationService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var loadedProfile = await profileLoader.LoadAsync(cancellationToken);
        var snapshot = await profileStore.SaveAsync(
            loadedProfile,
            timeProvider.GetUtcNow(),
            cancellationToken);
        ProfileLoaded(snapshot.ProfileVersion, snapshot.SchemaVersion);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        EventId = 13,
        Level = LogLevel.Information,
        Message = "Candidate profile version {ProfileVersion} using schema {SchemaVersion} is ready.")]
    private partial void ProfileLoaded(int profileVersion, int schemaVersion);
}
