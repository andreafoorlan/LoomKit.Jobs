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

    public static ServiceProvider Build(Action<JobConsumerOptionsBuilder>? configureConsumer = null, Action<JobQueueOptionsBuilder>? configureQueue = null)
    {
        var services = new ServiceCollection();

        // every Defaults/* type (JobScheduler, JobConsumer, InProcessJobQueue, middlewares) takes
        // an ILogger<T>, unlike the sibling libraries' senders/dispatchers - without this, DI
        // resolution fails as soon as ActivatorUtilities tries to construct any of them
        services.AddLogging();

        services.AddScoped<IJobHandler<PingJob, JobStatus>, PingJobHandler>();
        services.AddScoped<IJobHandler<TraceJob, JobStatus<List<string>>>, TraceJobHandler>();
        services.AddScoped<IJobHandler<RetryingJob, JobStatus>, RetryingJobHandler>();

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
