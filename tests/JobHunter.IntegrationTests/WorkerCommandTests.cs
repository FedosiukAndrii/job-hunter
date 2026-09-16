using JobHunter.Application.Orchestration;
using JobHunter.Domain.Sources;
using JobHunter.Worker;

namespace JobHunter.IntegrationTests;

public sealed class WorkerCommandTests
{
    [Fact]
    public void RunOnceRequiresAndSeparatesSourceArgument()
    {
        var parsed = WorkerCommand.TryParse(
            [
                "run-once",
                "--source",
                "dou",
                "--Storage:DataDirectory",
                "C:\\JobHunterData"
            ],
            out var command);

        Assert.True(parsed);
        Assert.Equal(WorkerCommandKind.RunOnce, command.Kind);
        Assert.Equal("dou", command.Source);
        Assert.Equal(
            ["--Storage:DataDirectory", "C:\\JobHunterData"],
            command.ConfigurationArguments);
    }

    [Fact]
    public void RunOnceWithoutSourceIsRejected()
    {
        Assert.False(
            WorkerCommand.TryParse(
                ["run-once", "--Storage:DataDirectory", "C:\\JobHunterData"],
                out _));
    }

    [Fact]
    public void DoctorAcceptsConfigurationArguments()
    {
        Assert.True(
            WorkerCommand.TryParse(
                ["doctor", "--Storage:DataDirectory", "C:\\JobHunterData"],
                out var command));
        Assert.Equal(WorkerCommandKind.Doctor, command.Kind);
    }

    [Theory]
    [InlineData(0, 1, 0, 0)]
    [InlineData(0, 0, 1, 0)]
    [InlineData(0, 0, 0, 1)]
    [InlineData(0, 0, 0, 0)]
    public void RunOnceRejectsAnyNonSuccessfulOutcome(
        int succeeded,
        int partial,
        int blocked,
        int failed)
    {
        var summary = new ScanBatchSummary(
            1,
            succeeded + partial + blocked + failed,
            succeeded,
            partial,
            blocked,
            failed,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0);

        Assert.Throws<OperatorCommandException>(
            () => RunOnceCompletionService.EnsureSuccessful(
                SourceName.Dou,
                summary));
    }

    [Fact]
    public void RunOnceAcceptsOnlyFullySuccessfulOutcome()
    {
        var summary = new ScanBatchSummary(
            1,
            1,
            1,
            0,
            0,
            0,
            1,
            1,
            1,
            1,
            0,
            0,
            0,
            0);

        RunOnceCompletionService.EnsureSuccessful(SourceName.Dou, summary);
    }
}
