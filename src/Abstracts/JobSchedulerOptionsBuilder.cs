using LoomKit.Jobs.Contracts;

namespace LoomKit.Jobs.Abstracts;

public abstract class JobSchedulerOptionsBuilder<TJobSchedulerOptions>
    where TJobSchedulerOptions : JobSchedulerOptions, new()
{
    private readonly Dictionary<string, JobQueueOptions> _jobQueueOptions;
    private readonly Dictionary<string, JobConsumerOptions> _jobConsumerOptions;
    private readonly LinkedList<Type> _jobSchedulerSeeders;

    public JobSchedulerOptionsBuilder()
    {
        // inits
        _jobQueueOptions = new();
        _jobConsumerOptions = new();
        _jobSchedulerSeeders = [];
    }

    public JobSchedulerOptionsBuilder<TJobSchedulerOptions> UseQueue<TJobQueue>(string queueName, Action<JobQueueOptionsBuilder> jobQueueOptionsBuilderAction)
        where TJobQueue : IJobQueue
    {
        // validate queue name
        if (string.IsNullOrWhiteSpace(queueName))
            throw new ArgumentException("Queue name must not be null or empty", nameof(queueName));

        // ensure queue name is not already taken
        if (_jobQueueOptions.ContainsKey(queueName))
            throw new InvalidOperationException($"Queue name {queueName} already taken");

        // create job queue options builder
        var jobQueueOptionsBuilder = new JobQueueOptionsBuilder()
        {
            JobQueueName = queueName,
            JobQueueType = typeof(TJobQueue)
        };

        // let the caller set options
        jobQueueOptionsBuilderAction.Invoke(jobQueueOptionsBuilder);

        // add queue options to scheduler options
        _jobQueueOptions.Add(queueName, jobQueueOptionsBuilder.Build());

        return this;
    }

    public JobSchedulerOptionsBuilder<TJobSchedulerOptions> UseConsumer<TJobConsumer>(string consumerName, string queueName, Action<JobConsumerOptionsBuilder> jobConsumerOptionsBuilderAction)
        where TJobConsumer : IJobConsumer
    {
        // validate names
        if (string.IsNullOrWhiteSpace(consumerName))
            throw new ArgumentException("Consumer name must not be null or empty", nameof(consumerName));

        if (string.IsNullOrWhiteSpace(queueName))
            throw new ArgumentException("Queue name must not be null or empty", nameof(queueName));

        // ensure consumer name is not already taken
        if (_jobConsumerOptions.ContainsKey(consumerName))
            throw new InvalidOperationException($"Consumer name {consumerName} already taken");

        // create job consumer options builder
        var jobConsumerOptionsBuilder = new JobConsumerOptionsBuilder()
        {
            JobConsumerName = consumerName,
            JobConsumerType = typeof(TJobConsumer),
            JobQueueName = queueName
        };

        // let the caller set options
        jobConsumerOptionsBuilderAction.Invoke(jobConsumerOptionsBuilder);

        // add consumer options to scheduler options
        _jobConsumerOptions.Add(consumerName, jobConsumerOptionsBuilder.Build());

        return this;
    }

    public JobSchedulerOptionsBuilder<TJobSchedulerOptions> UseJobSchedulerSeeder<TJobSchedulerSeeder>()
        where TJobSchedulerSeeder : IJobSchedulerSeeder
    {
        // add seeder type
        _jobSchedulerSeeders.AddLast(typeof(TJobSchedulerSeeder));

        return this;
    }

    public virtual TJobSchedulerOptions Build()
    {
        return new TJobSchedulerOptions()
        {
            JobQueueOptions = new Dictionary<string, JobQueueOptions>(_jobQueueOptions),
            JobConsumerOptions = new Dictionary<string, JobConsumerOptions>(_jobConsumerOptions),
            JobSchedulerSeeders = _jobSchedulerSeeders.ToList()
        };
    }
}
