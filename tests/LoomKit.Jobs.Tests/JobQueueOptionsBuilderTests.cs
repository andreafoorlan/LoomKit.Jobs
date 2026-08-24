using LoomKit.Jobs.Abstracts;

namespace LoomKit.Jobs.Tests;

public class JobQueueOptionsBuilderTests
{
    [Fact]
    public void Build_UsesDefaultValues_WhenNotOverridden()
    {
        var builder = new JobQueueOptionsBuilder
        {
            JobQueueName = "queue-a",
            JobQueueType = typeof(LoomKit.Jobs.Defaults.InProcessJobQueue)
        };

        var options = builder.Build();

        Assert.Equal(5, options.MaxJobRetries);
        Assert.Equal(1000, options.JobAwaitCheckInterval);
        Assert.Equal(5000, options.JobRetryInterval);
    }

    [Fact]
    public void Build_UsesOverriddenValues()
    {
        var builder = new JobQueueOptionsBuilder
        {
            JobQueueName = "queue-a",
            JobQueueType = typeof(LoomKit.Jobs.Defaults.InProcessJobQueue),
            MaxJobRetries = 1,
            JobAwaitCheckInterval = 10,
            JobRetryInterval = 20
        };

        var options = builder.Build();

        Assert.Equal(1, options.MaxJobRetries);
        Assert.Equal(10, options.JobAwaitCheckInterval);
        Assert.Equal(20, options.JobRetryInterval);
    }
}
