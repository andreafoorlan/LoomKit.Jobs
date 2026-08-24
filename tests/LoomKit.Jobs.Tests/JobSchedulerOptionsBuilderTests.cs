using LoomKit.Jobs.Defaults;

namespace LoomKit.Jobs.Tests;

public class JobSchedulerOptionsBuilderTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void UseQueue_ThrowsArgumentException_WhenQueueNameIsNullOrEmpty(string? queueName)
    {
        var builder = new DefaultJobSchedulerOptionsBuilder();

        Assert.Throws<ArgumentException>(() => builder.UseQueue<InProcessJobQueue>(queueName!, _ => { }));
    }

    [Fact]
    public void UseQueue_ThrowsInvalidOperationException_WhenQueueNameAlreadyTaken()
    {
        var builder = new DefaultJobSchedulerOptionsBuilder()
            .UseQueue<InProcessJobQueue>("queue-a", _ => { });

        Assert.Throws<InvalidOperationException>(() => builder.UseQueue<InProcessJobQueue>("queue-a", _ => { }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void UseConsumer_ThrowsArgumentException_WhenConsumerNameIsNullOrEmpty(string? consumerName)
    {
        var builder = new DefaultJobSchedulerOptionsBuilder();

        Assert.Throws<ArgumentException>(() => builder.UseConsumer<JobConsumer>(consumerName!, "queue-a", _ => { }));
    }

    [Fact]
    public void UseConsumer_ThrowsInvalidOperationException_WhenConsumerNameAlreadyTaken()
    {
        var builder = new DefaultJobSchedulerOptionsBuilder()
            .UseQueue<InProcessJobQueue>("queue-a", _ => { })
            .UseConsumer<JobConsumer>("consumer-a", "queue-a", _ => { });

        Assert.Throws<InvalidOperationException>(() => builder.UseConsumer<JobConsumer>("consumer-a", "queue-a", _ => { }));
    }

    [Fact]
    public void UseJobSchedulerSeeder_AddsSeederType_ToBuiltOptions()
    {
        var builder = new DefaultJobSchedulerOptionsBuilder()
            .UseJobSchedulerSeeder<NoopSeeder>();

        var options = builder.Build();

        Assert.Contains(typeof(NoopSeeder), options.JobSchedulerSeeders);
    }

    [Fact]
    public void Build_ProducesQueueAndConsumerOptions_WithConfiguredValues()
    {
        var builder = new DefaultJobSchedulerOptionsBuilder()
            .UseQueue<InProcessJobQueue>("queue-a", q => q.MaxJobRetries = 7)
            .UseConsumer<JobConsumer>("consumer-a", "queue-a", c => c.UseScopedServiceProvider = true);

        var options = builder.Build();

        Assert.Equal(7, options.JobQueueOptions["queue-a"].MaxJobRetries);
        Assert.Equal("queue-a", options.JobConsumerOptions["consumer-a"].JobQueueName);
        Assert.True(options.JobConsumerOptions["consumer-a"].UseScopedServiceProvider);
    }

    private sealed class NoopSeeder : LoomKit.Jobs.Contracts.IJobSchedulerSeeder
    {
        public Task SeedJobs(LoomKit.Jobs.Contracts.IJobScheduler jobScheduler) => Task.CompletedTask;
    }
}
