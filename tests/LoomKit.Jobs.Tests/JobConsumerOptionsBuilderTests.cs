using LoomKit.Jobs.Abstracts;
using LoomKit.Jobs.Tests.Fixtures;

namespace LoomKit.Jobs.Tests;

public class JobConsumerOptionsBuilderTests
{
    private static JobConsumerOptionsBuilder NewBuilder() => new()
    {
        JobConsumerName = "consumer-a",
        JobConsumerType = typeof(LoomKit.Jobs.Defaults.JobConsumer),
        JobQueueName = "queue-a"
    };

    [Fact]
    public void UseJobMiddleware_ThrowsArgumentNullException_WhenTypeIsNull()
    {
        var builder = NewBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.UseJobMiddleware(null!));
    }

    [Fact]
    public void UseJobMiddleware_ThrowsArgumentException_WhenTypeIsNotOpenGeneric()
    {
        var builder = NewBuilder();

        Assert.Throws<ArgumentException>(() => builder.UseJobMiddleware(typeof(FirstMiddleware<PingJob, LoomKit.Jobs.Models.JobStatus>)));
    }

    [Fact]
    public void UseJobMiddleware_ThrowsArgumentException_WhenTypeDoesNotDeriveFromJobMiddleware()
    {
        var builder = NewBuilder();

        Assert.Throws<ArgumentException>(() => builder.UseJobMiddleware(typeof(List<>)));
    }

    [Fact]
    public void ClearJobMiddlewares_RemovesPreviouslyAddedMiddlewares()
    {
        var builder = NewBuilder()
            .UseJobMiddleware(typeof(FirstMiddleware<,>))
            .ClearJobMiddlewares();

        var options = builder.Build();

        Assert.Empty(options.JobMiddlewares);
    }

    [Fact]
    public void Build_PreservesMiddlewareRegistrationOrder()
    {
        var builder = NewBuilder()
            .UseJobMiddleware(typeof(FirstMiddleware<,>))
            .UseJobMiddleware(typeof(SecondMiddleware<,>));

        var options = builder.Build();

        Assert.Equal([typeof(FirstMiddleware<,>), typeof(SecondMiddleware<,>)], options.JobMiddlewares);
    }

    [Fact]
    public void Build_DefaultsUseScopedServiceProvider_ToFalse()
    {
        var builder = NewBuilder();

        var options = builder.Build();

        Assert.False(options.UseScopedServiceProvider);
    }
}
