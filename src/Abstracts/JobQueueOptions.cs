namespace LoomKit.Jobs.Abstracts;

public class JobQueueOptions
{
    public required string JobQueueName { get; init; }

    public required Type JobQueueType { get; init; }

    public int MaxJobRetries { get; init; } = 5;

    public int JobAwaitCheckInterval { get; init; } = 1000;

    public int JobRetryInterval { get; init; } = 5000;
}
