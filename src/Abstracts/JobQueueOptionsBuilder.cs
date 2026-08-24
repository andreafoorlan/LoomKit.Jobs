namespace LoomKit.Jobs.Abstracts;

public class JobQueueOptionsBuilder
{
    public required string JobQueueName { get; init; }
    public required Type JobQueueType { get; init; }
    public int MaxJobRetries { get; set; } = 5;
    public int JobAwaitCheckInterval { get; set; } = 1000;
    public int JobRetryInterval { get; set; } = 5000;

    public JobQueueOptions Build()
    {
        return new JobQueueOptions()
        {
            JobQueueName = JobQueueName,
            JobQueueType = JobQueueType,
            MaxJobRetries = MaxJobRetries,
            JobAwaitCheckInterval = JobAwaitCheckInterval,
            JobRetryInterval = JobRetryInterval
        };
    }
}
