using LoomKit.Jobs.Abstracts;
using LoomKit.Jobs.Contracts;
using LoomKit.Jobs.Defaults;
using LoomKit.Jobs.Models;
using Microsoft.Extensions.DependencyInjection;

namespace LoomKit.Jobs.Tests.Fixtures;

internal static class TestServiceProviderFactory
{
    public const string QueueName = "test-queue";
    public const string ConsumerName = "test-consumer";

    // Same wiring as Build, but on a custom queue implementation instead of InProcessJobQueue - for
    // tests that need a queue behaving differently from the in-process one (e.g. a database-backed
    // queue that lets the cancellation exception escape from AwaitForJobScheduleAsync).
    public static ServiceProvider BuildWithQueue<TQueue>(Action<IServiceCollection>? configureServices = null)
        where TQueue : IJobQueue
    {
        var services = new ServiceCollection();

        services.AddLogging();

        configureServices?.Invoke(services);

        services.AddDefaultJobScheduler(builder => builder
            .UseQueue<TQueue>(QueueName, _ => { })
            .UseConsumer<JobConsumer>(ConsumerName, QueueName, _ => { }));

        return services.BuildServiceProvider();
    }

    public static ServiceProvider Build(
        Action<JobConsumerOptionsBuilder>? configureConsumer = null,
        Action<JobQueueOptionsBuilder>? configureQueue = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();

        // every Defaults/* type (JobScheduler, JobConsumer, InProcessJobQueue, middlewares) takes
        // an ILogger<T>, unlike the sibling libraries' senders/dispatchers - without this, DI
        // resolution fails as soon as ActivatorUtilities tries to construct any of them
        services.AddLogging();

        services.AddScoped<IJobHandler<PingJob, JobStatus>, PingJobHandler>();
        services.AddScoped<IJobHandler<TraceJob, JobStatus<List<string>>>, TraceJobHandler>();
        services.AddScoped<IJobHandler<RetryingJob, JobStatus>, RetryingJobHandler>();
        services.AddScoped<IJobHandler<ScopedDependencyJob, JobStatus>, ScopedDependencyJobHandler>();

        configureServices?.Invoke(services);

        services.AddDefaultJobScheduler(builder => builder
            .UseQueue<InProcessJobQueue>(QueueName, q =>
            {
                // short polling interval so tests don't have to wait out the 1000ms default
                q.JobAwaitCheckInterval = 10;
                configureQueue?.Invoke(q);
            })
            .UseConsumer<JobConsumer>(ConsumerName, QueueName, c => configureConsumer?.Invoke(c)));

        return services.BuildServiceProvider();
    }
}
