namespace Common.Observability.Tests;

public sealed class CorrelationScopeTests
{
    [Fact]
    public void Current_IsNull_WhenNoScopeActive()
    {
        Assert.Null(CorrelationScope.Current);
    }

    [Fact]
    public void Begin_MakesContextCurrent_AndDisposeRestores()
    {
        var run = RunContext.NewRun();

        using (CorrelationScope.Begin(run))
        {
            Assert.Same(run, CorrelationScope.Current);
        }

        Assert.Null(CorrelationScope.Current);
    }

    [Fact]
    public void Scopes_Nest()
    {
        var outer = RunContext.NewRun();
        var inner = RunContext.NewRun();

        using (CorrelationScope.Begin(outer))
        {
            Assert.Same(outer, CorrelationScope.Current);

            using (CorrelationScope.Begin(inner))
            {
                Assert.Same(inner, CorrelationScope.Current);
            }

            Assert.Same(outer, CorrelationScope.Current);
        }

        Assert.Null(CorrelationScope.Current);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var scope = CorrelationScope.Begin(RunContext.NewRun());
        scope.Dispose();
        scope.Dispose();

        Assert.Null(CorrelationScope.Current);
    }

    [Fact]
    public void Begin_NullContext_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => CorrelationScope.Begin(null!));
    }

    [Fact]
    public async Task Current_FlowsAcrossAsyncContinuations()
    {
        var run = RunContext.NewRun();

        using (CorrelationScope.Begin(run))
        {
            await Task.Yield();
            Assert.Same(run, CorrelationScope.Current);
        }
    }
}
