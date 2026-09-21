using JobHunter.JobSources.JobSpy.Configuration;

namespace JobHunter.ContractTests.JobSpy;

public sealed class JobSpyOptionsValidatorTests
{
    [Fact]
    public void ValidateRejectsEnabledSourceWithoutRiskAcknowledgement()
    {
        var options = new JobSpyOptions
        {
            Enabled = true,
            ExperimentalAcknowledged = false
        };

        var result = new JobSpyOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains(
                "ExperimentalAcknowledged",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("https://jobspy.example.com")]
    [InlineData("http://user:password@127.0.0.1:8080")]
    public void ValidateRejectsNonLoopbackOrCredentialBearingEndpoint(string endpoint)
    {
        var options = new JobSpyOptions
        {
            Endpoint = endpoint
        };

        var result = new JobSpyOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("loopback", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void ValidateRejectsResultLimitOutsideSidecarContract(int maximumResults)
    {
        var options = new JobSpyOptions
        {
            MaximumResults = maximumResults
        };

        var result = new JobSpyOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("between 1 and 50", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRejectsOverlongOptionalLocation()
    {
        var options = new JobSpyOptions
        {
            Location = new string('x', 257)
        };

        var result = new JobSpyOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("Location", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateRejectsControlCharactersInOptionalLocation()
    {
        var options = new JobSpyOptions
        {
            Location = "Ukraine\0"
        };

        var result = new JobSpyOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("Location", StringComparison.Ordinal));
    }
}
