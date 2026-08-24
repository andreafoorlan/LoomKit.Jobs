using LoomKit.Jobs.Abstracts;
using LoomKit.Jobs.Contracts;
using LoomKit.Jobs.Events;
using LoomKit.Jobs.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LoomKit.Jobs.Defaults;

public class JobScheduler : JobScheduler<DefaultJobSchedulerOptions>
{
    public override event EventHandler<JobScheduledEventArgs>? JobScheduled;
    public override event EventHandler<JobStartedEventArgs>? JobStarted;
    public override event EventHandler<JobEndedEventArgs>? JobEnded;
    public override event EventHandler<JobExceptionEventArgs>? JobException;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<JobScheduler> _logger;
    private readonly Dictionary<string, IJobQueue> _jobQueues;
    private readonly Dictionary<string, IJobConsumer> _jobConsumers;

    public JobScheduler(DefaultJobSchedulerOptions jobSchedulerOptions, IServiceProvider serviceProvider, ILogger<JobScheduler> logger)
        : base(jobSchedulerOptions)
    {
        // deps
        _serviceProvider = serviceProvider;
        _logger = logger;

        // inits
        _jobQueues = new Dictionary<string, IJobQueue>();
        _jobConsumers = new Dictionary<string, IJobConsumer>();
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // start job queues from options
        foreach (var (queueName, queueOptions) in _jobSchedulerOptions.JobQueueOptions)
        {
            EnsureOrThrow(queueOptions.JobQueueType is not null, $"Queue type for queue {queueName} is null.");
            EnsureOrThrow(!_jobQueues.ContainsKey(queueName), $"Queue with name {queueName} already exists.");

            // create queue instance, injecting the options and resolving the rest from DI -
            // ActivatorUtilities picks the constructor matching the supplied arguments and DI
            // registrations, instead of the ad-hoc (and buggy) constructor selection this used to do
            var queue = (IJobQueue)ActivatorUtilities.CreateInstance(_serviceProvider, queueOptions.JobQueueType!, queueOptions);

            EnsureOrThrow(_jobQueues.TryAdd(queueName, queue), $"Queue with name {queueName} not added to job queues.");

            await queue.StartAsync(cancellationToken);
        }

        // start job consumers from options
        foreach (var (consumerName, consumerOptions) in _jobSchedulerOptions.JobConsumerOptions)
        {
            EnsureOrThrow(consumerOptions.JobConsumerType is not null, $"Consumer type for consumer {consumerName} is null.");
            EnsureOrThrow(!_jobConsumers.ContainsKey(consumerName), $"Consumer with name {consumerName} already exists.");
            EnsureOrThrow(_jobQueues.TryGetValue(consumerOptions.JobQueueName, out var jobQueue), $"Job queue {consumerOptions.JobQueueName} for consumer with name {consumerName} does not exists.");

            // create consumer instance, injecting the options, its queue, and resolving the rest from DI
            var consumer = (IJobConsumer)ActivatorUtilities.CreateInstance(_serviceProvider, consumerOptions.JobConsumerType!, consumerOptions, jobQueue!);

            EnsureOrThrow(_jobConsumers.TryAdd(consumerName, consumer), $"Consumer with name {consumerName} not added to job consumers.");

            await consumer.StartAsync(cancellationToken);
        }

        // seed jobs
        foreach (var jobSchedulerSeederType in _jobSchedulerOptions.JobSchedulerSeeders)
        {
            // get seeder from DI
            var jobSchedulerSeeder = (IJobSchedulerSeeder)_serviceProvider.GetRequiredService(jobSchedulerSeederType);

            // seed queue with jobs
            await jobSchedulerSeeder.SeedJobs(this);
        }

        //
        _logger.LogInformation("[{methodName}] Job scheduler started", nameof(StartAsync));
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // stop consumers first so no new job is picked up while queues are still draining
        foreach (var (_, consumer) in _jobConsumers)
        {
            await consumer.StopAsync(cancellationToken);
        }

        // then stop the queues themselves
        foreach (var (_, queue) in _jobQueues)
        {
            await queue.StopAsync(cancellationToken);
        }

        //
        _logger.LogInformation("[{methodName}] Job scheduler stopped", nameof(StopAsync));
    }

    public override Task<Dictionary<string, IJobQueue>> ListQueuesAsync(CancellationToken cancellationToken = default)
    {
        //
        return Task.FromResult(_jobQueues.ToDictionary());
    }

    public override Task<Dictionary<string, IJobConsumer>> ListConsumersAsync(CancellationToken cancellationToken = default)
    {
        //
        return Task.FromResult(_jobConsumers.ToDictionary());
    }

    public override async Task EnqueueJobScheduleAsync(string queueName, JobSchedule jobSchedule, CancellationToken cancellationToken = default)
    {
        // try get the queue by name
        if (!_jobQueues.TryGetValue(queueName, out var jobQueue))
            throw new InvalidOperationException($"Queue {queueName} does not exists");

        //
        await jobQueue.EnqueueJobScheduleAsync(jobSchedule, cancellationToken);
    }

    public override Task<List<JobSchedule>> ListConsumingJobSchedulesAsync(CancellationToken cancellationToken = default)
    {
        //
        var consumingJobSchedules = new List<JobSchedule>();

        // get not null consumers current job schedules
        foreach (var (consumerName, consumer) in _jobConsumers)
        {
            if (consumer.CurrentJobSchedule is not null)
                consumingJobSchedules.Add(consumer.CurrentJobSchedule);
        }

        //
        return Task.FromResult(consumingJobSchedules);
    }

    public override async Task<Dictionary<string, List<JobSchedule>>> ListQueuedJobSchedulesAsync(Func<IQueryable<JobSchedule>, IQueryable<JobSchedule>> queryBuilder, CancellationToken cancellationToken)
    {
        //
        var allQueuesJobSchedules = new Dictionary<string, List<JobSchedule>>();

        //
        foreach (var queueName in _jobQueues.Keys)
        {
            //
            var queueJobSchedules = await _jobQueues[queueName].ListJobSchedulesAsync(queryBuilder, cancellationToken);

            //
            allQueuesJobSchedules.Add(queueName, queueJobSchedules);
        }

        //
        return allQueuesJobSchedules;
    }

    public override async Task<Dictionary<string, List<JobSchedule>>> ListQueuedJobSchedulesAsync(string queueName, Func<IQueryable<JobSchedule>, IQueryable<JobSchedule>> queryBuilder, CancellationToken cancellationToken)
    {
        // try get the queue by name
        if (!_jobQueues.TryGetValue(queueName, out var jobQueue))
            throw new InvalidOperationException($"Queue {queueName} does not exists");

        //
        var listJobSchedules = await jobQueue.ListJobSchedulesAsync(queryBuilder, cancellationToken);

        //
        var queueJobSchedules = new Dictionary<string, List<JobSchedule>>
        {
            { queueName, listJobSchedules }
        };

        //
        return queueJobSchedules;
    }

    public override Task<JobQueueOptions?> GetJobQueueOptionsAsync(string queueName, CancellationToken cancellationToken = default)
    {
        // try get the queue by name
        if (!_jobQueues.TryGetValue(queueName, out var jobQueue))
            throw new InvalidOperationException($"Queue {queueName} does not exists");

        //
        return Task.FromResult(jobQueue.JobQueueOptions);
    }

    public override Task<JobConsumerOptions?> GetJobConsumerOptionsAsync(string consumerName, CancellationToken cancellationToken = default)
    {
        // try get the consumer by name
        if (!_jobConsumers.TryGetValue(consumerName, out var jobConsumer))
            throw new InvalidOperationException($"Consumer {consumerName} does not exists");

        //
        return Task.FromResult(jobConsumer.JobConsumerOptions);
    }

    public override async Task<Dictionary<string, long>> RemoveQueuedJobSchedulesAsync(Predicate<JobSchedule> predicate, CancellationToken cancellationToken = default)
    {
        //
        var allQueuesRemovedCount = new Dictionary<string, long>();

        //
        foreach (var queueName in _jobQueues.Keys)
        {
            //
            var queueRemoveJobSchedulesCount = await _jobQueues[queueName].RemoveJobSchedulesAsync(predicate, cancellationToken);

            //
            allQueuesRemovedCount.Add(queueName, queueRemoveJobSchedulesCount);
        }

        //
        return allQueuesRemovedCount;
    }

    public override async Task<Dictionary<string, long>> RemoveQueuedJobSchedulesAsync(string queueName, Predicate<JobSchedule> predicate, CancellationToken cancellationToken = default)
    {
        // try get the queue by name
        if (!_jobQueues.TryGetValue(queueName, out var jobQueue))
            throw new InvalidOperationException($"Queue {queueName} does not exists");

        //
        var queueRemoveJobSchedulesCount = await jobQueue.RemoveJobSchedulesAsync(predicate, cancellationToken);

        //
        var queueRemovedCount = new Dictionary<string, long>
        {
            { queueName, queueRemoveJobSchedulesCount }
        };

        //
        return queueRemovedCount;
    }

    public override Task NotifyJobScheduled(string queueName, JobSchedule jobSchedule)
    {
        //
        JobScheduled?.Invoke(this, new JobScheduledEventArgs()
        {
            QueueName = queueName,
            JobSchedule = jobSchedule
        });

        //
        return Task.CompletedTask;
    }

    public override Task NotifyJobStarted(string queueName, string consumerName, JobSchedule jobSchedule)
    {
        //
        JobStarted?.Invoke(this, new JobStartedEventArgs()
        {
            QueueName = queueName,
            ConsumerName = consumerName,
            JobSchedule = jobSchedule
        });

        //
        return Task.CompletedTask;
    }

    public override Task NotifyJobEnded(string queueName, string consumerName, JobSchedule jobSchedule)
    {
        //
        JobEnded?.Invoke(this, new JobEndedEventArgs()
        {
            QueueName = queueName,
            ConsumerName = consumerName,
            JobSchedule = jobSchedule
        });

        //
        return Task.CompletedTask;
    }

    public override Task NotifyJobException(string queueName, string consumerName, JobSchedule jobSchedule, Exception exception)
    {
        //
        JobException?.Invoke(this, new JobExceptionEventArgs()
        {
            QueueName = queueName,
            ConsumerName = consumerName,
            JobSchedule = jobSchedule,
            Exception = exception
        });

        //
        return Task.CompletedTask;
    }

    private void EnsureOrThrow(bool condition, string message)
    {
        if (condition)
            return;

        _logger.LogError("{message}", message);
        throw new InvalidOperationException(message);
    }
}
