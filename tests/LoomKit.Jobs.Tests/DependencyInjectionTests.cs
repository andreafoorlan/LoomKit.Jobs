using LoomKit.Jobs.Contracts;
using LoomKit.Jobs.Defaults;
using LoomKit.Jobs.Extensions;
using LoomKit.Jobs.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LoomKit.Jobs.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public async Task AddJobHandlersFromAssemblies_RegistersHandlers_ResolvableEndToEnd()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddJobHandlersFromAssemblies(ServiceLifetime.Scoped, typeof(PingJobHandler).Assembly);

        services.AddDefaultJobScheduler(builder => builder
            .UseQueue<InProcessJobQueue>(TestServiceProviderFactory.QueueName, q => q.JobAwaitCheckInterval = 10)
            .UseConsumer<JobConsumer>(TestServiceProviderFactory.ConsumerName, TestServiceProviderFactory.QueueName, _ => { }));

        using var provider = services.BuildServiceProvider();
        var scheduler = provider.GetRequiredService<IJobScheduler>();

        await SchedulerTestHarness.RunAsync(scheduler, async () =>
        {
            var job = new PingJob();
            await scheduler.ScheduleNowAsync(TestServiceProviderFactory.QueueName, job);

            var completed = await Wait.UntilAsync(() => job.Trace.Count > 0, TimeSpan.FromSeconds(2));

            Assert.True(completed);
            Assert.Equal(["handler"], job.Trace);
        });
    }

    [Fact]
    public void AddJobHandlersFromAssemblies_DoesNotRegister_OpenGenericMiddlewareClasses()
    {
        var services = new ServiceCollection();
        services.AddJobHandlersFromAssemblies(ServiceLifetime.Scoped, typeof(PingJobHandler).Assembly);

        // FirstMiddleware<,>/SecondMiddleware<,> also implement IJobHandler<TJob,TJobStatus>, but
        // as open generics they must never be picked up by the scan and registered as if they
        // were handlers.
        Assert.DoesNotContain(services, d => d.ImplementationType == typeof(FirstMiddleware<,>));
        Assert.DoesNotContain(services, d => d.ImplementationType == typeof(SecondMiddleware<,>));
    }

    [Fact]
    public void AddJobHandlersFromAssemblies_DoesNotDuplicate_WhenCalledTwiceOrGivenOverlappingAssemblies()
    {
        var services = new ServiceCollection();
        var assembly = typeof(PingJobHandler).Assembly;

        services.AddJobHandlersFromAssemblies(ServiceLifetime.Scoped, assembly, assembly);
        services.AddJobHandlersFromAssemblies(ServiceLifetime.Scoped, assembly);

        var pingRegistrations = services.Count(d => d.ServiceType == typeof(IJobHandler<PingJob, LoomKit.Jobs.Models.JobStatus>) && d.ImplementationType == typeof(PingJobHandler));

        Assert.Equal(1, pingRegistrations);
    }

    [Fact]
    public void AddJobHandlersFromAssemblies_RespectsRequestedServiceLifetime()
    {
        var services = new ServiceCollection();
        services.AddJobHandlersFromAssemblies(ServiceLifetime.Singleton, typeof(PingJobHandler).Assembly);

        var descriptor = services.Single(d => d.ServiceType == typeof(IJobHandler<PingJob, LoomKit.Jobs.Models.JobStatus>));

        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddJobHandlersFromAssemblies_ThrowsArgumentNullException_WhenAssembliesIsNull()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => services.AddJobHandlersFromAssemblies(ServiceLifetime.Scoped, (System.Reflection.Assembly[])null!));
    }

    [Fact]
    public void AddDefaultJobScheduler_RegistersSchedulerAsSingleton_AndAsHostedService()
    {
        var services = new ServiceCollection();
        services.AddDefaultJobScheduler(_ => { });

        var schedulerDescriptor = services.Single(d => d.ServiceType == typeof(IJobScheduler));
        Assert.Equal(ServiceLifetime.Singleton, schedulerDescriptor.Lifetime);

        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService));
    }
}
