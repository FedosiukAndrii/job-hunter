using JobHunter.Domain.Jobs;

namespace JobHunter.Domain.Tests.Jobs;

public sealed class JobScoreTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(72)]
    [InlineData(100)]
    public void ConstructorAcceptsInclusiveScoreRange(int value)
    {
        var score = new JobScore(value);

        Assert.Equal(value, score.Value);
        Assert.Equal(value, (int)score);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void ConstructorRejectsValuesOutsideScoreRange(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new JobScore(value));
    }
}
