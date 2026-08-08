namespace Common.Observability.Tests;

public sealed class RunContextTests
{
    [Fact]
    public void NewRun_CorrelationIdEqualsRunId()
    {
        var run = RunContext.NewRun();

        Assert.False(string.IsNullOrWhiteSpace(run.RunId));
        Assert.Equal(run.RunId, run.CorrelationId);
    }

    [Fact]
    public void NewRun_ProducesUniqueRunIds()
    {
        Assert.NotEqual(RunContext.NewRun().RunId, RunContext.NewRun().RunId);
    }

    [Theory]
    [InlineData(null, "c")]
    [InlineData("", "c")]
    [InlineData("r", "  ")]
    public void Constructor_BlankArgument_Throws(string? runId, string? correlationId)
    {
        Assert.ThrowsAny<ArgumentException>(() => new RunContext(runId!, correlationId!));
    }

    [Fact]
    public void Equality_ByValue()
    {
        Assert.Equal(new RunContext("r", "c"), new RunContext("r", "c"));
        Assert.NotEqual(new RunContext("r", "c"), new RunContext("r", "d"));
    }
}
